using System.Text.Json;
using CSweet.WorkManagement.Contracts;

namespace CSweet.Agent.SDK.Tests;

public sealed class DeliveryReviewStageBoundaryTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Theory]
    [InlineData("ready", true)]
    [InlineData("code-defect", false)]
    [InlineData("validation-missing", false)]
    public async Task Native_candidate_review_keeps_integration_downstream_and_rejects_real_candidate_failures(string scenario, bool approved)
    {
        var itemId = Guid.NewGuid(); var plan = Guid.NewGuid(); var repository = Guid.NewGuid();
        const string criterion = "An exact Phaser version and reproducible lockfile are committed and integrated.";
        var assignment = JsonSerializer.Deserialize<WorkExecutionAssignment>("{}")! with {
            Scope = "Task", ItemId = itemId, BoardId = Guid.NewGuid(), SprintId = Guid.NewGuid(), SprintExecutionId = Guid.NewGuid(),
            PolicyRevisionId = Guid.NewGuid(), StageExecutionId = Guid.NewGuid(), AttemptId = Guid.NewGuid(),
            AssignmentRevision = 1, PlanningRevision = 1, StageKey = "technical-review", DeliveryPlanId = plan };
        var item = JsonSerializer.Deserialize<WorkItem>("{}")! with {
            Id = itemId, PlanningRevision = 1, Planning = new(["Pin Phaser"], [criterion]),
            Delivery = new(repository, ["Pin Phaser"], [criterion]) { DeliveryPlanId = plan, BaseBranch = "story" },
            StageAssignments = [new("technical-review", "AgentInstallation", AgentInstallationId: Guid.Parse("00000000-0000-0000-0000-000000000001"))] };
        var reference = new GitMergeReview(Guid.NewGuid(), repository, itemId, "PRISM BREAK", new string('a', 40), null, "", [], [], "AwaitingValidation") {
            TargetBranch = "story", TargetCommitSha = new string('b', 40), PatchDigest = new string('c', 64), ChangedFiles = ["package.json", "package-lock.json"] };
        // A page assessed under the former policy must not be reused under the corrected contract.
        var oldIdentity = BranchDiffTechnicalReview.Hash(JsonSerializer.Serialize(new { Policy = "bounded-review-2026-10-15",
            item.Id, item.PlanningRevision, assignment.AssignmentRevision, assignment.DeliveryPlanId, assignment.ScopeRevision,
            item.Planning, item.Delivery, reference.PublicationId, reference.CandidateCommitSha, reference.TargetCommitSha, reference.PatchDigest }, Json));
        var oldKey = $"task-review:{item.Id:N}:{oldIdentity[..32]}:0";
        var reads = new List<string>(); var saved = new List<string>();
        var runtime = new AgentTestRuntime()
            .RegisterCapability<WorkItemReference, WorkItem>(WorkManagementCapabilityNames.ItemRead, (_, _) => Task.FromResult(item))
            .RegisterCapability<ReviewGitMergeRequest, GitMergeReview>(GitMergeCapabilities.Review, (r, _) => Task.FromResult(r.DiffRead is null ? reference :
                reference with { DiffPage = new(reference.PatchDigest!, 0, 90, new string('x', 90), null) }))
            .RegisterCapability<AgentOperatingStateReadRequest, AgentOperatingStateReadResponse>(PlatformCapabilities.AgentOperatingStateRead, (r, _) => {
                reads.Add(r.StateKey); Assert.NotEqual(oldKey, r.StateKey);
                return Task.FromResult(new AgentOperatingStateReadResponse(null));
            })
            .RegisterCapability<AgentOperatingStateWriteRequest, AgentOperatingStateResponse>(PlatformCapabilities.AgentOperatingStateWrite, (r, _) => {
                saved.Add(r.StateKey); var now = DateTimeOffset.UtcNow;
                return Task.FromResult(new AgentOperatingStateResponse(Guid.NewGuid(), r.StateKey, r.SchemaId, r.SchemaVersion, r.Status,
                    r.SourceRevisions, r.ConditionCodes, r.DecisionFingerprint, r.OpenCommitmentCorrelations, r.AttentionReviewId, r.Payload, 1, now, now));
            });
        var defect = new BranchDiffTechnicalReview.Issue("package.json", scenario == "code-defect" ? "Phaser uses a range" : "Focused installed-version check is missing",
            "Visible candidate evidence", "Correct the candidate pin or provide the required executed check");
        using var client = new BranchDiffTechnicalReviewTests.Client((messages, input) => {
            var policy = messages[0].Text;
            Assert.Contains("pre-integration candidate source review", policy);
            Assert.Contains("Missing target-branch integration alone is not a code-review defect", policy);
            Assert.Contains("independent QA and final acceptance remain mandatory downstream gates", policy);
            if (input.TryGetProperty("page", out _))
                return JsonSerializer.Serialize(new BranchDiffTechnicalReview.PageContent("Candidate pin/lockfile inspected; integration remains downstream.",
                    approved ? [] : [defect], ["No integrated target revision is claimed by candidate review."], []), Json);
            var findings = input.GetProperty("findings").EnumerateArray().ToArray();
            return JsonSerializer.Serialize(new BranchDiffTechnicalReview.FinalContent(approved, "Candidate obligations reviewed; platform integration and QA remain downstream.", [],
                findings.Select(x => new BranchDiffTechnicalReview.Resolution(x.GetProperty("findingId").GetString()!, "unresolved", "Actual candidate defect remains", [0])).ToArray(),
                [new(criterion, approved, approved ? "Exact candidate pin and lockfile with supplied focused validation; integration and QA are pending downstream." : "Required candidate evidence is unsatisfied.", [0])]), Json);
        });
        var result = await DeliveryTaskTechnicalReview.ExecuteAsync(assignment, runtime.CreateContext(), client, default);
        Assert.True(result.Succeeded);
        var outcome = result.Value!.Value.Deserialize<WorkExecutionOutcomeV1>(Json)!;
        Assert.Equal(approved ? "approved" : "rejected", outcome.OutcomeCode);
        Assert.Equal(criterion, outcome.Output.GetProperty("criteria")[0].GetProperty("criterionText").GetString());
        Assert.Equal(reference.CandidateCommitSha, outcome.Output.GetProperty("sourceCommitSha").GetString());
        Assert.Equal(reference.TargetCommitSha, outcome.Output.GetProperty("targetCommitSha").GetString());
        Assert.Equal(2, client.Calls); Assert.Single(reads); Assert.Single(saved);
        Assert.Equal(reads[0], saved[0]);
    }
}
