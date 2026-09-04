namespace CSweet.Agent.SDK.Tests;

public sealed class RoleTaxonomyTests
{
    [Fact]
    public void SoftwareArchitect_CanFillSpecializedGameArchitectSlot()
    {
        var role = new ResourceChangeRole(
            "game-tech-architect", "Game", "Game Architect", "Own game architecture", 1, 1, "Now",
            ["software-architecture.design.v2"], false, Guid.NewGuid(), null)
        {
            RoleCategoryKey = "software-architect",
            PreferredSpecializationKeys = ["game-development"]
        };
        var teammate = new AgentTeammate(
            Guid.NewGuid().ToString("D"), "Architect", "Agent", "Architect", "Game Architect",
            "DirectReport", "Active")
        {
            DeclaredRoleKeys = ["software-architect"],
            SpecializationKeys = ["technical-planning"]
        };

        Assert.True(RoleTaxonomy.CanFill(role, teammate));
        Assert.Equal(0, RoleTaxonomy.PreferredSpecializationScore(role, teammate));
    }

    [Fact]
    public void SelectAssignment_RequiresExactRoleAndSkills_ThenUsesPreferredWipAndStableId()
    {
        var firstId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var secondId = Guid.Parse("00000000-0000-0000-0000-000000000002");
        var requirements = new CSweet.WorkManagement.Contracts.WorkAssignmentRequirements(
            "game-engineer", ["gameplay-programming"], ["engine-integration"], ["work.execution.run.v1"]);
        AgentTeammate Candidate(Guid id, params string[] skills) => new(
            id.ToString("D"), "Engineer", "Agent", null, null, "DirectReport", "Active")
        {
            AgentInstallationId = id,
            RuntimeEligibility = "Eligible",
            DeclaredRoleKeys = ["game-engineer"],
            SpecializationKeys = skills,
            EffectiveCapabilities = ["work.execution.run.v1"]
        };

        var selected = RoleTaxonomy.SelectAssignment(
            [Candidate(firstId, "gameplay-programming"), Candidate(secondId, "gameplay-programming", "engine-integration")],
            requirements,
            new Dictionary<Guid, int> { [firstId] = 0, [secondId] = 4 });

        Assert.Equal(secondId, selected!.Teammate.AgentInstallationId);
        Assert.Equal(1, selected.PreferredSpecializationCount);
    }
}
