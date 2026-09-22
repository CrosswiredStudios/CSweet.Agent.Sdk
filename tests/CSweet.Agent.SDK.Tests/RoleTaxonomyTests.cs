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

    [Theory]
    [InlineData("game-engineer", "software-developer", true)]
    [InlineData("software-developer", "game-engineer", true)]
    [InlineData("game-quality-assurance", "software-qa", true)]
    [InlineData("game-engineer", "software-architect", false)]
    public void DomainRoleMatchesItsCoreFamilyOnly(string required, string declared, bool expected)
    {
        Assert.Equal(expected, RoleTaxonomy.SatisfiesRole([declared], required));
    }

    [Fact]
    public void GeneralDeveloperCanTakeGameEngineeringWorkWhenSkillsArePreferred()
    {
        var developer = new AgentTeammate(Guid.NewGuid().ToString("D"), "Developer", "Agent",
            "Software Developer", "Game Engineer", "TeamMember", "Active")
        {
            AgentInstallationId = Guid.NewGuid(), RuntimeEligibility = "Eligible",
            DeclaredRoleKeys = ["software-developer"],
            EffectiveCapabilities = ["work.execution.run.v1"]
        };
        var preferred = new CSweet.WorkManagement.Contracts.WorkAssignmentRequirements(
            "game-engineer", [], ["gameplay-programming"], ["work.execution.run.v1"]);
        var required = preferred with { RequiredSpecializationKeys = ["gameplay-programming"] };

        Assert.True(RoleTaxonomy.IsEligible(developer, preferred));
        Assert.False(RoleTaxonomy.IsEligible(developer, required));
    }

    [Fact]
    public void SelectAssignment_RequiresCompatibleRoleAndSkills_ThenUsesPreferredWipAndStableId()
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
