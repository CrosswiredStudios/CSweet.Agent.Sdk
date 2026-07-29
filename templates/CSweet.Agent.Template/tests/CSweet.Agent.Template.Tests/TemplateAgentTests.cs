using CSweet.Agent.SDK;

namespace CSweet.Agent.Template.Tests;

public sealed class TemplateAgentTests
{
    [Fact]
    public async Task PrimaryCapability_ReturnsTypedResultAndProgress()
    {
        var runtime = new AgentTestRuntime();

        var result = await runtime.ExecuteCapabilityAsync(
            new TemplateAgent(),
            TemplateAgent.PrimaryCapability,
            new TemplateRequest("hello"));

        Assert.True(result.Succeeded);
        Assert.Equal("Processed: hello", result.Value!.Value.GetProperty("message").GetString());
        Assert.Single(runtime.Progress);
    }

    [Fact]
    public async Task MissingInput_FailsSafely()
    {
        var result = await new AgentTestRuntime().ExecuteCapabilityAsync(
            new TemplateAgent(),
            TemplateAgent.PrimaryCapability,
            new { unexpected = true });

        Assert.False(result.Succeeded);
        Assert.Equal("input is required.", result.Error);
    }

    [Fact]
    public async Task NonObjectPayload_FailsSafely()
    {
        using var payload = System.Text.Json.JsonDocument.Parse("\"not-an-object\"");
        var runtime = new AgentTestRuntime();

        var result = await new TemplateAgent().ExecuteCapabilityAsync(
            new AgentCapabilityRequest(
                Guid.NewGuid(),
                TemplateAgent.PrimaryCapability,
                payload.RootElement.Clone()),
            runtime.CreateContext(),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("The request payload is not valid.", result.Error);
    }

    [Fact]
    public async Task UnsupportedCapability_FailsSafely()
    {
        var result = await new AgentTestRuntime().ExecuteCapabilityAsync(
            new TemplateAgent(),
            "example.unsupported.v1",
            new TemplateRequest("hello"));

        Assert.False(result.Succeeded);
        Assert.Contains("not supported", result.Error);
    }

    [Fact]
    public async Task Cancellation_IsHonored()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new AgentTestRuntime().ExecuteCapabilityAsync(
                new TemplateAgent(),
                TemplateAgent.PrimaryCapability,
                new TemplateRequest("hello"),
                cancellation.Token));
    }

    [Fact]
    public async Task UngrantedPlatformCapability_IsDenied()
    {
        var context = new AgentTestRuntime().CreateContext();

        var exception = await Assert.ThrowsAsync<PlatformCapabilityException>(
            () => context.Platform.ReadBusinessProfileAsync());

        Assert.Equal(PlatformCapabilityErrorCode.Denied, exception.Code);
    }
}
