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

    private sealed class ConfigurableAgent : CSweetAgentBase
    {
        public override string AgentId => "com.example.configurable";
        public override string Version => "1.0.0";
        public string? CurrentModel => Settings.GetString("llmModel");

        protected override AgentConfigurationBuilder Configure(AgentConfigurationBuilder builder) =>
            builder
                .LlmProvider("llmProviderId", "Provider")
                .LlmModel("llmModel", "Model", "llmProviderId");
    }
}
