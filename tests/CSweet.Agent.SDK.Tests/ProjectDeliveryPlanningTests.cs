using CSweet.WorkManagement.Contracts;
using Xunit;

namespace CSweet.Agent.SDK.Tests;

public sealed class ProjectDeliveryPlanningTests
{
    private static readonly Guid Project = Guid.NewGuid();
    private static readonly ProjectDeliveryPlanRequest Request = new(Project, Guid.NewGuid(), "current", "Build the approved demo");
    private static ProjectDeliveryPlan Valid() => new(Project, "current", "Isolated game logic, renderer and integration tests", [
        new("loop", "Loop", "Implement game loop", ["Loop"], ["Loop tested"], [], [], 1, 3),
        new("effects", "Effects", "Effects on hits", ["Effects"], ["Effects tested"], [], ["loop"], 2, 3)]);
    [Fact] public void Valid_plan_contains_a_dependency_ordered_series_of_sprints() => Assert.Empty(ProjectDeliveryPlanning.Validate(Valid(), Request));
    [Theory]
    [InlineData("scope")]
    [InlineData("unknown-dependency")]
    [InlineData("cycle")]
    [InlineData("duplicate-key")]
    [InlineData("missing-criteria")]
    [InlineData("missing-sprint")]
    public void Invalid_plans_cannot_become_executable_work(string scenario)
    {
        var plan = Valid(); var items = plan.Items.ToArray();
        if (scenario == "scope") plan = plan with { Fingerprint = "stale" };
        if (scenario == "unknown-dependency") items[1] = items[1] with { Dependencies = ["invented"] };
        if (scenario == "cycle") items[0] = items[0] with { Dependencies = ["effects"] };
        if (scenario == "duplicate-key") items[1] = items[1] with { Key = "loop" };
        if (scenario == "missing-criteria") items[0] = items[0] with { AcceptanceCriteria = [] };
        if (scenario == "missing-sprint") items[1] = items[1] with { Sprint = 3 };
        Assert.NotEmpty(ProjectDeliveryPlanning.Validate(plan with { Items = items }, Request));
    }
    [Fact]
    public void Administrative_revision_changes_preserve_the_plan_but_scope_changes_invalidate_it()
    {
        var project = new WorkstreamDetail(Project, "Demo", "Playable", ["Verified"], "concept", "Approved", Guid.NewGuid(), null, null, null, "brief", 2, null, "pin", 1);
        Assert.Equal(ProjectDeliveryPlanning.Fingerprint(project), ProjectDeliveryPlanning.Fingerprint(project with { Revision = 2, LifecycleStage = "prototype" }));
        Assert.NotEqual(ProjectDeliveryPlanning.Fingerprint(project), ProjectDeliveryPlanning.Fingerprint(project with { Outcome = "Different game" }));
        Assert.NotEqual(ProjectDeliveryPlanning.Fingerprint(project), ProjectDeliveryPlanning.Fingerprint(project with { ProfileDefinitionDigest = "new pin" }));
    }
    [Fact]
    public void Review_requires_exact_candidate_and_coverage_of_every_criterion()
    {
        var valid = new ProjectDeliveryReviewDecision("candidate", true, "Verified against patch", [], [new("Playable", true, "Concrete patch and test evidence")]);
        ProjectDeliveryReview.Validate(valid, "candidate", ["Playable"]);
        Assert.Throws<InvalidOperationException>(() => ProjectDeliveryReview.Validate(valid, "new-candidate", ["Playable"]));
        Assert.Throws<InvalidOperationException>(() => ProjectDeliveryReview.Validate(valid, "candidate", ["Playable", "Performant"]));
        Assert.Throws<InvalidOperationException>(() => ProjectDeliveryReview.Validate(valid with { Criteria = [new("Playable", false, "Missing evidence")] }, "candidate", ["Playable"]));
        Assert.Throws<InvalidOperationException>(() => ProjectDeliveryReview.Validate(valid with { Approved = false }, "candidate", ["Playable"]));
    }
}
