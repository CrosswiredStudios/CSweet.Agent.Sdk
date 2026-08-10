using CSweet.Agent.Contracts.Packaging;
using CSweet.WorkManagement.Contracts;

namespace CSweet.Agent.SDK.Tests;

public sealed class AgentRuntimePersonalTodoRecoveryTests
{
    [Fact]
    public void StartupSweepRequiresBothSubscriptionAndClaimDeclaration()
    {
        var subscribed = Manifest(
            [PersonalTodoEvents.Available],
            [new AgentRequiredCapability(PersonalTodoCapabilities.Claim)]);
        var noSubscription = Manifest(
            [],
            [new AgentRequiredCapability(PersonalTodoCapabilities.Claim)]);
        var noClaim = Manifest([PersonalTodoEvents.Available], []);

        Assert.True(AgentRuntimeWorker<TestAgent>.ShouldRecoverPersonalTodoOnStartup(subscribed));
        Assert.False(AgentRuntimeWorker<TestAgent>.ShouldRecoverPersonalTodoOnStartup(noSubscription));
        Assert.False(AgentRuntimeWorker<TestAgent>.ShouldRecoverPersonalTodoOnStartup(noClaim));
    }

    private static AgentManifest Manifest(
        IReadOnlyList<string> events,
        IReadOnlyList<AgentRequiredCapability> requires) => new()
        {
            Id = "com.example.recovery",
            Name = "Recovery agent",
            Version = "1.0.0",
            Publisher = new AgentPublisher("example", "Example"),
            Runtime = new AgentRuntimeManifest(),
            Protocol = new AgentProtocolManifest("2.0", "2.0"),
            Events = new AgentEventManifest(events),
            Requires = requires
        };

    private sealed class TestAgent : CSweetAgentBase
    {
        public override string AgentId => "com.example.recovery";
        public override string Version => "1.0.0";
    }
}
