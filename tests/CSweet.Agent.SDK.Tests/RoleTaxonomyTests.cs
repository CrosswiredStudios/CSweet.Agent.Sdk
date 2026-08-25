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
}
