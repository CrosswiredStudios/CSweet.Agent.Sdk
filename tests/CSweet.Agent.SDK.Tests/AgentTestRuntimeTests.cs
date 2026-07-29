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

    [Fact]
    public async Task DeliversStableEventIdentityDistinctFromWorkIdentity()
    {
        var runtime = new AgentTestRuntime();
        var eventId = Guid.NewGuid();
        var agent = new CapturingEventAgent();

        await runtime.DeliverEventAsync(
            agent,
            "example.event.v1",
            new { },
            eventId);

        Assert.NotNull(agent.Message);
        Assert.Equal(eventId, agent.Message.EventId);
        Assert.NotEqual(agent.Message.WorkId, agent.Message.EventId);
    }

    [Fact]
    public async Task LifecycleClientCompletesOnboardingWithEnvelopeEventIdentity()
    {
        CompleteAgentOnboardingRequest? request = null;
        var runtime = new AgentTestRuntime()
            .RegisterCapability<CompleteAgentOnboardingRequest, CompleteAgentOnboardingResponse>(
                AgentLifecycleCapabilities.CompleteOnboarding,
                (value, _) =>
                {
                    request = value;
                    return Task.FromResult(
                        new CompleteAgentOnboardingResponse(true, DateTimeOffset.UtcNow));
                });
        var eventId = Guid.NewGuid();
        var message = new AgentEventEnvelope(
            Guid.NewGuid(),
            eventId,
            AgentLifecycleEvents.Onboarded,
            JsonSerializer.SerializeToElement(new { }),
            DateTimeOffset.UtcNow);

        var response = await runtime.CreateContext()
            .Platform
            .Lifecycle
            .CompleteOnboardingAsync(message);

        Assert.True(response.Completed);
        Assert.NotNull(request);
        Assert.Equal(eventId, request.EventId);
        Assert.NotEqual(message.WorkId, request.EventId);
    }

    [Fact]
    public async Task LifecycleClientRejectsNonOnboardingEvents()
    {
        var runtime = new AgentTestRuntime();
        var message = new AgentEventEnvelope(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "example.event.v1",
            JsonSerializer.SerializeToElement(new { }),
            DateTimeOffset.UtcNow);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            runtime.CreateContext()
                .Platform
                .Lifecycle
                .CompleteOnboardingAsync(message));
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

    private sealed class CapturingEventAgent : CSweetAgentBase
    {
        public override string AgentId => "com.example.capturing";
        public override string Version => "1.0.0";
        public AgentEventEnvelope? Message { get; private set; }

        public override Task HandleEventAsync(
            AgentEventEnvelope message,
            AgentRuntimeContext context,
            CancellationToken cancellationToken)
        {
            Message = message;
            return Task.CompletedTask;
        }
    }
}
