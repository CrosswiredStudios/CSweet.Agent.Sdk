using System.Text.Json;
using CSweet.WorkManagement.Contracts;

namespace CSweet.Agent.SDK.Tests;

public sealed class HierarchicalDeliveryTests
{
    [Fact]
    public void AggregateAssignmentsNeverFabricateSprintAuthority()
    {
        var assignment = Assignment(WorkExecutionScopes.Release);
        Assert.Null(assignment.SprintExecutionId); Assert.Null(assignment.SprintId); Assert.Null(assignment.PolicyRevisionId);
        Assert.Throws<InvalidOperationException>(assignment.ToTaskAssignment);
        var roundtrip = JsonSerializer.Deserialize<WorkExecutionAssignmentV2>(JsonSerializer.Serialize(assignment));
        Assert.Equal(assignment.DeliveryPlanId, roundtrip!.DeliveryPlanId);
        Assert.Equal(assignment.OrganizationUserId, roundtrip.OrganizationUserId);
        Assert.Equal(assignment.PlanningRevision, roundtrip.PlanningRevision);
    }

    [Fact]
    public void ApprovalCannotIgnoreARepositoryOrCriterion()
    {
        var digest = new string('a', 64);
        var valid = new WorkDeliveryReviewResult(digest, true, "Verified", [new("Works", true, "Observed exact candidate")], []);
        DeliveryScopeReview.Validate(valid, digest, ["Works"]);
        Assert.Throws<InvalidOperationException>(() => DeliveryScopeReview.Validate(valid with { CandidateDigest = new string('b', 64) }, digest, ["Works"]));
        Assert.Throws<InvalidOperationException>(() => DeliveryScopeReview.Validate(valid with { Criteria = [] }, digest, ["Works"]));
        Assert.Throws<InvalidOperationException>(() => DeliveryScopeReview.Validate(valid with { Findings = ["Unresolved regression"] }, digest, ["Works"]));
        Assert.Throws<InvalidOperationException>(() => DeliveryScopeReview.Validate(valid with { Approved = false }, digest, ["Works"]));
    }

    [Fact]
    public void NewSoftwarePolicyAlwaysRequiresQaAndIntegratesOnlyCodeTasks()
    {
        var column = Guid.NewGuid();
        var (stages, transitions) = HierarchicalWorkflows.Software(column, column, column, column, column);
        Assert.DoesNotContain(stages, x => x.Key.Contains("merge", StringComparison.Ordinal));
        Assert.Equal("technical-review", Assert.Single(transitions, x => x.FromStageKey == "development" && x.OutcomeCode == "completed").ToStageKey);
        Assert.Equal("quality", Assert.Single(transitions, x => x.OutcomeCode == "artifact-delivered").ToStageKey);
        Assert.Equal("quality", Assert.Single(transitions, x => x.FromStageKey == "task-integration").ToStageKey);
        Assert.Equal("quality", Assert.Single(transitions, x => x.ToStageKey == "done").FromStageKey);
        Assert.Equal("development", Assert.Single(transitions, x => x.FromStageKey == "quality" && x.OutcomeCode == "changes_requested").ToStageKey);
        Assert.Contains(WorkManagementCapabilityNames.ExecutionRunV1, WorkManagementCapabilityNames.All);
        Assert.Contains(WorkManagementCapabilityNames.ExecutionRunV2, WorkManagementCapabilityNames.All);
    }

    private static WorkExecutionAssignmentV2 Assignment(string scope) => new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, scope, Guid.NewGuid(), 2, null, null, 4, "Release", null,
        "quality", 1, 1, DateTimeOffset.UtcNow.AddHours(1), "Validate", JsonSerializer.SerializeToElement(new { }),
        JsonSerializer.SerializeToElement(new { }), [], []) { OrganizationUserId = Guid.NewGuid(), PlanningRevision = 3, PermittedOutcomes = ["approved", "changes_requested"] };
}
