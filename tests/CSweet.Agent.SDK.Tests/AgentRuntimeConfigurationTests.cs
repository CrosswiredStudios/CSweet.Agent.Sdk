using System.Text.Json;

namespace CSweet.Agent.SDK.Tests;

public sealed class AgentRuntimeConfigurationTests
{
    [Fact]
    public async Task RuntimeSessionConfiguration_IsAppliedBeforeAgentWork()
    {
        var agent = new ConfigurableAgent();
        var session = new AgentRuntimeSession(
            Guid.NewGuid().ToString("D"),
            DateTimeOffset.UtcNow.AddMinutes(10),
            1,
            null,
            new AgentRuntimeConfiguration(
                "1",
                new Dictionary<string, JsonElement>
                {
                    ["llmModel"] = JsonSerializer.SerializeToElement("configured-model")
                }));

        await AgentRuntimeWorker<ConfigurableAgent>.ApplyInitialConfigurationAsync(
            agent,
            session,
            new AgentTestRuntime().CreateContext(),
            CancellationToken.None);

        Assert.Equal("configured-model", agent.CurrentModel);
    }

    [Fact]
    public async Task RuntimeSessionConfiguration_RejectionFailsStartup()
    {
        var agent = new ConfigurableAgent();
        var session = new AgentRuntimeSession(
            Guid.NewGuid().ToString("D"),
            DateTimeOffset.UtcNow.AddMinutes(10),
            1,
            null,
            new AgentRuntimeConfiguration(
                "1",
                new Dictionary<string, JsonElement>
                {
                    ["unknown"] = JsonSerializer.SerializeToElement("value")
                }));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            AgentRuntimeWorker<ConfigurableAgent>.ApplyInitialConfigurationAsync(
                agent,
                session,
                new AgentTestRuntime().CreateContext(),
                CancellationToken.None));

        Assert.Contains("could not be applied", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LiveConfiguration_AtomicallyReplacesSnapshotAndCallsHook()
    {
        var agent = new ConfigurableAgent();
        var update = new AgentRuntimeConfiguration(
            "1",
            new Dictionary<string, JsonElement>
            {
                ["llmModel"] = JsonSerializer.SerializeToElement("new-model")
            },
            Guid.NewGuid(),
            12,
            "digest");

        var result = await agent.ApplyPlatformConfigurationAsync(update, ["llmModel"], CancellationToken.None);

        Assert.Equal(ConfigurationApplyResult.Applied, result);
        Assert.Equal("new-model", agent.CurrentModel);
        Assert.Equal(["llmModel"], agent.LastChangedKeys);
    }

    [Fact]
    public async Task LiveConfiguration_IgnoresStaleRevision()
    {
        var agent = new ConfigurableAgent();
        await agent.ApplyPlatformConfigurationAsync(new AgentRuntimeConfiguration(
            "1", new Dictionary<string, JsonElement> { ["llmModel"] = JsonSerializer.SerializeToElement("current") },
            Guid.NewGuid(), 3, "current"), ["llmModel"], CancellationToken.None);

        await agent.ApplyPlatformConfigurationAsync(new AgentRuntimeConfiguration(
            "1", new Dictionary<string, JsonElement> { ["llmModel"] = JsonSerializer.SerializeToElement("stale") },
            Guid.NewGuid(), 2, "stale"), ["llmModel"], CancellationToken.None);

        Assert.Equal("current", agent.CurrentModel);
        Assert.Equal(1, agent.CallbackCount);
    }

    [Fact]
    public async Task RelationalTokenBudget_RejectsOutputEqualToContext()
    {
        var agent = new BudgetAgent();
        var invalid = new AgentRuntimeConfiguration(
            "1",
            new Dictionary<string, JsonElement>
            {
                ["maxContextWindowTokens"] = JsonSerializer.SerializeToElement(128000),
                ["maxOutputTokens"] = JsonSerializer.SerializeToElement(128000)
            });

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            agent.ApplyPlatformConfigurationAsync(
                invalid, ["maxContextWindowTokens", "maxOutputTokens"], CancellationToken.None));

        Assert.Contains("must be less than", error.Message, StringComparison.Ordinal);
        Assert.Equal(32000, agent.ContextTokens);
        Assert.Equal(8000, agent.OutputTokens);
    }

    private sealed class ConfigurableAgent : CSweetAgentBase
    {
        public override string AgentId => "com.example.configurable";
        public override string Version => "1.0.0";
        public string? CurrentModel => Settings.GetString("llmModel");
        public IReadOnlyList<string> LastChangedKeys { get; private set; } = [];
        public int CallbackCount { get; private set; }

        protected override AgentConfigurationBuilder Configure(AgentConfigurationBuilder builder) =>
            builder
                .LlmProvider("llmProviderId", "Provider")
                .LlmModel("llmModel", "Model", "llmProviderId");

        protected override Task<ConfigurationApplyResult> OnConfigurationChangedAsync(
            AgentConfigurationChangedContext change,
            CancellationToken cancellationToken)
        {
            CallbackCount++;
            LastChangedKeys = change.ChangedKeys;
            return Task.FromResult(ConfigurationApplyResult.Applied);
        }
    }

    private sealed class BudgetAgent : CSweetAgentBase
    {
        public override string AgentId => "com.example.budget";
        public override string Version => "1.0.0";
        public int ContextTokens => Settings.GetInt32("maxContextWindowTokens");
        public int OutputTokens => Settings.GetInt32("maxOutputTokens");

        protected override AgentConfigurationBuilder Configure(AgentConfigurationBuilder builder) =>
            builder
                .Number("maxContextWindowTokens", "Context", true, defaultValue: 32000)
                .Number("maxOutputTokens", "Output", true, defaultValue: 8000,
                    lessThanFieldKey: "maxContextWindowTokens");
    }
}
