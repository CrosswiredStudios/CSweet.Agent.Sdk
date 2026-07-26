using CSweet.Agent.SDK;

namespace CSweet.Agent.SDK.Tests;

/// <summary>
/// Compile-checked counterparts of the authoring examples in docs/creating-an-agent.md.
/// Keep the documentation and these examples synchronized.
/// </summary>
public sealed class DocumentationSnippetTests
{
    [Fact]
    public async Task CreatingAnAgent_InMemoryRuntimeSnippetRunsWithoutCredentials()
    {
        var runtime = new AgentTestRuntime()
            .RegisterCapability<object, object>(
                "platform.example.read.v1",
                (request, token) => Task.FromResult<object>(new { value = 42 }));

        var result = await runtime.ExecuteCapabilityAsync(
            new ExampleAgent(),
            "example.echo.v1",
            new { text = "hello" });

        Assert.True(result.Succeeded);
        Assert.Equal(
            "hello",
            result.Value!.Value.GetProperty("value").GetProperty("text").GetString());
    }

    private sealed class ExampleAgent : CSweetAgentBase
    {
        public override string AgentId => "com.example.agent";
        public override string Version => "1.0.0";

        public override Task HandleEventAsync(
            AgentEventEnvelope message,
            AgentRuntimeContext context,
            CancellationToken cancellationToken) =>
            context.ReportProgressAsync(
                new { stage = "received", message.EventType },
                cancellationToken);

        protected override Task<AgentWorkResult> ExecuteCapabilityCoreAsync(
            AgentCapabilityRequest request,
            AgentRuntimeContext context,
            CancellationToken cancellationToken) =>
            Task.FromResult(request.Capability == "example.echo.v1"
                ? AgentWorkResult.Success(new { value = request.Arguments })
                : AgentWorkResult.Failure("Unsupported capability."));
    }
}
