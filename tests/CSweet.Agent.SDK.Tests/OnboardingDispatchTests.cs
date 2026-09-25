using System.Text.Json;

namespace CSweet.Agent.SDK.Tests;

public sealed class OnboardingDispatchTests
{
    [Fact]
    public async Task OnboardedEvent_DispatchesToOnboardedHook()
    {
        var agent = new RecordingOnboardingAgent();
        var onboarded = new AgentOnboardedEvent(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow);

        await new AgentTestRuntime().DeliverEventAsync(agent, AgentLifecycleEvents.Onboarded, onboarded);

        Assert.Equal(1, agent.OnboardedCount);
        Assert.Equal(onboarded, agent.LastOnboarded);
    }

    [Fact]
    public async Task UnknownEvent_DoesNotTriggerOnboardedHook()
    {
        var agent = new RecordingOnboardingAgent();

        await new AgentTestRuntime().DeliverEventAsync(agent, "example.event.v1", new { });

        Assert.Equal(0, agent.OnboardedCount);
    }

    private sealed class RecordingOnboardingAgent : CSweetAgentBase
    {
        public override string AgentId => "com.example.onboarding";
        public override string Version => "1.0.0";
        public int OnboardedCount { get; private set; }
        public AgentOnboardedEvent? LastOnboarded { get; private set; }

        protected override Task OnOnboardedAsync(
            AgentOnboardedEvent onboarded,
            AgentEventEnvelope message,
            AgentRuntimeContext context,
            CancellationToken cancellationToken)
        {
            OnboardedCount++;
            LastOnboarded = onboarded;
            return Task.CompletedTask;
        }
    }
}
