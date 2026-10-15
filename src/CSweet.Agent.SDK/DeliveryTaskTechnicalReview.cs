using System.Text.Json;
using CSweet.WorkManagement.Contracts;
using Microsoft.Extensions.AI;

namespace CSweet.Agent.SDK;

/// <summary>Independent task review binds both the published source and the current story target.</summary>
public static class DeliveryTaskTechnicalReview
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public static async Task<AgentWorkResult> ExecuteAsync(WorkExecutionAssignment assignment,
        AgentRuntimeContext context, IChatClient client, CancellationToken ct)
    {
        if (assignment.StageKey != "technical-review") throw new InvalidOperationException("This operation requires assigned Technical Review.");
        var item = await context.Platform.Work.ReadItemAsync(new(assignment.BoardId, assignment.TaskItemId), ct);
        if (item.Delivery?.DeliveryPlanId is null || item.Delivery.DeliveryKind != "Code" || item.PlanningRevision <= 0 ||
            item.StageAssignments.Single(x => x.StageKey == "technical-review").AgentInstallationId.ToString() != context.InstallationId ||
            item.StageAssignments.Where(x => x.StageKey is "development" or "specialist-execution").Any(x => x.AgentInstallationId.ToString() == context.InstallationId))
            throw new InvalidOperationException("Technical Review requires the current independent reviewer and authorized code delivery.");
        var candidate = await context.Platform.SourceControl.ReviewMergeAsync(new(item.Id, assignment.AssignmentRevision,
            $"task-review:{assignment.StageExecutionId:N}:{assignment.AttemptId:N}") { IncludePatch = false }, ct);
        if (candidate.TargetBranch != item.Delivery.BaseBranch || candidate.TargetCommitSha is not { Length: 40 or 64 } ||
            candidate.CandidateCommitSha.Length is not (40 or 64))
            throw new InvalidOperationException("The review must include the exact source, authorized story target and reviewable patch.");
        var result = await BranchDiffTechnicalReview.ExecuteAsync(candidate, item.Planning,
            (read, token) => context.Platform.SourceControl.ReviewMergeAsync(new(item.Id, assignment.AssignmentRevision,
                $"task-review:{assignment.StageExecutionId:N}:{assignment.AttemptId:N}:{read.Offset}")
                { IncludePatch = false, DiffRead = read }, token), client, ct);
        if (result.SourceCommitSha != candidate.CandidateCommitSha || result.TargetCommitSha != candidate.TargetCommitSha ||
            string.IsNullOrWhiteSpace(result.Summary) || result.Findings.Any(string.IsNullOrWhiteSpace) ||
            (result.Approved ? result.Findings.Count != 0 : result.Findings.Count == 0))
            throw new InvalidOperationException("Technical Review must bind both exact commits and return a consistent decision.");
        return AgentWorkResult.Success(new WorkExecutionOutcomeV1(assignment.StageExecutionId, assignment.AttemptId,
            WorkExecutionDispositions.Completed, result.Approved ? "approved" : "rejected", result.Summary,
            JsonSerializer.SerializeToElement(result, Json),
            [new("commit", "Reviewed task source", result.SourceCommitSha), new("target-commit", "Reviewed story target", result.TargetCommitSha)], result.Findings));
    }
    private sealed record Decision(string SourceCommitSha, string TargetCommitSha, bool Approved, string Summary, IReadOnlyList<string> Findings);
}
