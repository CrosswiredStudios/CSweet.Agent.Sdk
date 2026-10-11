using CSweet.WorkManagement.Contracts;

namespace CSweet.Agent.SDK.Tests;

public sealed class BuildReadinessStaffingTests
{
    [Theory]
    [InlineData("game-technical-director", "game-technical-director")]
    [InlineData("software-developer", "software-developer")]
    [InlineData("game-engineer", "software-developer")]
    public void An_updated_existing_builder_satisfies_the_gate_without_a_release_hire(string role, string expected)
    {
        Assert.Equal(expected, HierarchicalProjectDelivery.SelectBuildReadinessRole(Roster(Member(role))));
    }

    [Fact]
    public void Prefer_release_specialist_then_technical_director_then_developer()
    {
        var release = Member("game-build-release-engineer") with { EffectiveCapabilities = [WorkManagementCapabilityNames.ExecutionRunV2] };
        var td = Member("game-technical-director"); var dev = Member("software-developer");
        Assert.Equal("game-build-release-engineer", HierarchicalProjectDelivery.SelectBuildReadinessRole(Roster(dev, td, release)));
        Assert.Equal("game-technical-director", HierarchicalProjectDelivery.SelectBuildReadinessRole(Roster(dev, td)));
    }

    [Theory]
    [InlineData("game-technical-director", "legacy")]
    [InlineData("software-developer", "legacy")]
    [InlineData("software-developer", "offline")]
    [InlineData("software-developer", "unavailable")]
    [InlineData("software-developer", "no-v2")]
    [InlineData("game-quality-assurance", "current")]
    [InlineData("manager", "current")]
    public void Unsupported_or_ineligible_agents_cannot_satisfy_the_gate(string role, string condition)
    {
        var member = Member(role);
        member = condition switch {
            "legacy" => member with { EffectiveCapabilities = [WorkManagementCapabilityNames.ExecutionRunV2] },
            "offline" => member with { RuntimeEligibility = "Unavailable" },
            "unavailable" => member with { IsAvailable = false },
            "no-v2" => member with { EffectiveCapabilities = [DeliveryBuildReadiness.ExecutionCapability] },
            _ => member
        };
        Assert.Throws<DeliveryStaffingException>(() => HierarchicalProjectDelivery.SelectBuildReadinessRole(Roster(member)));
    }

    private static AgentTeammate Member(string role) => new(Guid.NewGuid().ToString(), role, "Agent", role, role, "Peer", "Online") {
        AgentInstallationId = Guid.NewGuid(), IsAvailable = true, RuntimeEligibility = "Eligible", DeclaredRoleKeys = [role],
        EffectiveCapabilities = [WorkManagementCapabilityNames.ExecutionRunV2, DeliveryBuildReadiness.ExecutionCapability] };
    private static AgentTeamContext Roster(params AgentTeammate[] members) => new(Guid.NewGuid().ToString(), "Game", "Game", 1,
        Guid.NewGuid().ToString(), "Producer", members, [], members.Length, false);
}
