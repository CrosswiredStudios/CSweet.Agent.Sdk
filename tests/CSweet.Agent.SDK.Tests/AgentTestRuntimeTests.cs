using System.Text.Json;

namespace CSweet.Agent.SDK.Tests;

public sealed class AgentTestRuntimeTests
{
    [Fact]
    public async Task Executes_agent_capability_without_network_credentials()
    {
        var runtime = new AgentTestRuntime();
        var result = await runtime.ExecuteCapabilityAsync(
            new EchoAgent(),
            "example.echo.v1",
            new { value = "hello" });

        Assert.True(result.Succeeded);
        Assert.Equal("hello", result.Value!.Value.GetProperty("value").GetString());
    }

    [Fact]
    public async Task Denies_unregistered_platform_capability()
    {
        var runtime = new AgentTestRuntime();
        var context = runtime.CreateContext();

        var action = () => context.Platform.InvokeAsync<object, JsonElement>(
            "platform.hidden.v1",
            new { });

        await Assert.ThrowsAsync<PlatformCapabilityException>(action);
    }

    [Fact]
    public async Task Captures_progress()
    {
        var runtime = new AgentTestRuntime();
        await runtime.DeliverEventAsync(new EchoAgent(), "example.event.v1", new { });

        Assert.Single(runtime.Progress);
        Assert.Equal("handled", runtime.Progress[0].GetProperty("stage").GetString());
    }

    private sealed class EchoAgent : CSweetAgentBase
    {
        public override string AgentId => "com.example.echo";
        public override string Version => "1.0.0";

        public override Task HandleEventAsync(
            AgentEventEnvelope message,
            AgentRuntimeContext context,
            CancellationToken cancellationToken) =>
            context.ReportProgressAsync(new { stage = "handled" }, cancellationToken);

        protected override Task<AgentWorkResult> ExecuteCapabilityCoreAsync(
            AgentCapabilityRequest request,
            AgentRuntimeContext context,
            CancellationToken cancellationToken) =>
            Task.FromResult(request.Capability == "example.echo.v1"
                ? AgentWorkResult.Success(request.Arguments)
                : AgentWorkResult.Failure("Unsupported."));
    }
}
