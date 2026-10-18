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
        if (item.Id != assignment.TaskItemId || item.Planning is null || item.Delivery?.DeliveryPlanId is null ||
            item.Delivery.DeliveryKind != "Code" || item.PlanningRevision <= 0 ||
            item.PlanningRevision != assignment.PlanningRevision ||
            item.Delivery.DeliveryPlanId != assignment.DeliveryPlanId ||
            item.StageAssignments.Single(x => x.StageKey == "technical-review").AgentInstallationId.ToString() != context.InstallationId ||
            item.StageAssignments.Where(x => x.StageKey is "development" or "specialist-execution").Any(x => x.AgentInstallationId.ToString() == context.InstallationId))
            throw new InvalidOperationException("Technical Review requires the current independent reviewer and authorized code delivery.");
        var candidate = await context.Platform.SourceControl.ReviewMergeAsync(new(item.Id, assignment.AssignmentRevision,
            $"task-review:{assignment.StageExecutionId:N}:{assignment.AttemptId:N}") { IncludePatch = false }, ct);
        if (candidate.TargetBranch != item.Delivery.BaseBranch || candidate.TargetCommitSha is not { Length: 40 or 64 } ||
            candidate.CandidateCommitSha.Length is not (40 or 64))
            throw new InvalidOperationException("The review must include the exact source, authorized story target and reviewable patch.");
        var identity = BranchDiffTechnicalReview.Hash(JsonSerializer.Serialize(new { BranchDiffTechnicalReview.Policy,
            item.Id, item.PlanningRevision, assignment.AssignmentRevision, assignment.DeliveryPlanId, assignment.ScopeRevision,
            item.Planning, item.Delivery, candidate.PublicationId, candidate.CandidateCommitSha, candidate.TargetCommitSha,
            candidate.PatchDigest }, Json));
        var stateKey = $"task-review:{item.Id:N}:{identity[..32]}";
        async Task<BranchDiffTechnicalReview.PageReview?> Load(int offset, CancellationToken token)
        {
            var state = await context.Platform.ReadOperatingStateAsync<BranchDiffTechnicalReview.PageReview>($"{stateKey}:{offset}", token);
            if (state is not null && (state.SchemaId != BranchDiffTechnicalReview.Policy || state.SchemaVersion != 1))
                throw new InvalidOperationException("The retained review has a different review policy.");
            return state?.Payload;
        }
        async Task Save(BranchDiffTechnicalReview.PageReview page, CancellationToken token)
        {
            var key = $"{stateKey}:{page.Offset}";
            try
            {
                var stored = await context.Platform.WriteOperatingStateAsync(new WriteAgentOperatingStateRequest<BranchDiffTechnicalReview.PageReview>(
                    key, BranchDiffTechnicalReview.Policy, 1, "Active", new Dictionary<string,string> { ["review"] = identity }, [],
                    identity, [], assignment.StageExecutionId, page, null, key + ":accepted"), token);
                // An idempotent replay may return the first writer's different judgment without a CAS conflict.
                // Never synthesize from content that differs from the accepted durable ledger.
                if (JsonSerializer.Serialize(stored.Payload, Json) != JsonSerializer.Serialize(page, Json))
                    throw new InvalidOperationException("A concurrent review already retained different page evidence; resume from the accepted ledger.");
            }
            catch (PlatformCapabilityException error) when (error.Code == PlatformCapabilityErrorCode.Conflict)
            {
                var concurrent = await Load(page.Offset, token);
                if (JsonSerializer.Serialize(concurrent, Json) != JsonSerializer.Serialize(page, Json)) throw;
            }
        }
        var result = await BranchDiffTechnicalReview.ExecuteAsync(candidate, item.Planning,
            (read, token) => context.Platform.SourceControl.ReviewMergeAsync(new(item.Id, assignment.AssignmentRevision,
                $"task-review:{assignment.StageExecutionId:N}:{assignment.AttemptId:N}:{read.Offset}")
                { IncludePatch = false, DiffRead = read }, token), client, ct, Load, Save);
        var latest = await context.Platform.SourceControl.ReviewMergeAsync(new(item.Id, assignment.AssignmentRevision,
            $"task-review:{assignment.AttemptId:N}:final") { IncludePatch = false }, ct);
        var current = await context.Platform.Work.ReadItemAsync(new(assignment.BoardId, item.Id), ct);
        if (latest.PublicationId != candidate.PublicationId || latest.CandidateCommitSha != candidate.CandidateCommitSha ||
            latest.TargetCommitSha != candidate.TargetCommitSha || latest.PatchDigest != candidate.PatchDigest ||
            current.PlanningRevision != item.PlanningRevision ||
            JsonSerializer.Serialize(current.Planning, Json) != JsonSerializer.Serialize(item.Planning, Json) ||
            JsonSerializer.Serialize(current.Delivery, Json) != JsonSerializer.Serialize(item.Delivery, Json) ||
            current.StageAssignments.Single(x => x.StageKey == "technical-review").AgentInstallationId.ToString() != context.InstallationId)
            throw new InvalidOperationException("The source, target, planning, delivery or independent assignment changed during review.");
        if (result.SourceCommitSha != candidate.CandidateCommitSha || result.TargetCommitSha != candidate.TargetCommitSha ||
            string.IsNullOrWhiteSpace(result.Summary) || result.Findings.Any(string.IsNullOrWhiteSpace) ||
            (result.Approved ? result.Findings.Count != 0 : result.Findings.Count == 0))
            throw new InvalidOperationException("Technical Review must bind both exact commits and return a consistent decision.");
        return AgentWorkResult.Success(Outcome(assignment, result));
    }
    internal static WorkExecutionOutcomeV1 Outcome(WorkExecutionAssignment assignment, BranchDiffTechnicalReview.Decision result)
    {
        // Stage/card summaries have a separate 4096-character storage contract. Keep the full
        // accepted report in Output; this explicitly marked preview is presentation only.
        const string suffix = "… [Full review summary is retained in output.summary.]";
        var summary = result.Summary.Length <= 4096 ? result.Summary : result.Summary[..(4096 - suffix.Length)] + suffix;
        return new(assignment.StageExecutionId, assignment.AttemptId,
            WorkExecutionDispositions.Completed, result.Approved ? "approved" : "rejected", summary,
            JsonSerializer.SerializeToElement(result, Json),
            [new("commit", "Reviewed task source", result.SourceCommitSha), new("target-commit", "Reviewed story target", result.TargetCommitSha)], result.Findings);
    }
}
