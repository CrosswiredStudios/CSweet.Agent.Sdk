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
        // File judgments only see this scope and their own source. Publication identity and validation
        // receipts are checked on every final decision. A new target or changed assignment cannot inherit them.
        var scope = BranchDiffTechnicalReview.Hash(JsonSerializer.Serialize(new { BranchDiffTechnicalReview.Policy,
            assignment.BoardId, item.Id, item.PlanningRevision, assignment.DeliveryPlanId, assignment.ScopeRevision,
            context.InstallationId, item.Planning, item.Delivery, candidate.RepositoryId, candidate.SourceBranch,
            candidate.TargetBranch, candidate.TargetCommitSha }, Json));
        string Key(ReviewSection section) =>
            $"task-file:{item.Id:N}:{BranchDiffTechnicalReview.Hash(scope + ":" + section.CacheKey)}";
        async Task<BranchDiffTechnicalReview.FileReceipt?> Load(ReviewSection section, CancellationToken token)
        {
            var state = await context.Platform.ReadOperatingStateAsync<BranchDiffTechnicalReview.FileReceipt>(Key(section), token);
            if (state is not null && (state.SchemaId != BranchDiffTechnicalReview.Policy || state.SchemaVersion != 1 ||
                state.DecisionFingerprint != scope))
                throw new InvalidOperationException("Retained file evidence belongs to a different review scope.");
            return state?.Payload;
        }
        async Task Save(ReviewSection section, BranchDiffTechnicalReview.FileReceipt receipt, CancellationToken token)
        {
            var key = Key(section);
            BranchDiffTechnicalReview.FileReceipt? accepted;
            try
            {
                var stored = await context.Platform.WriteOperatingStateAsync(new WriteAgentOperatingStateRequest<BranchDiffTechnicalReview.FileReceipt>(
                    key, BranchDiffTechnicalReview.Policy, 1, "Active", new Dictionary<string,string> { ["scope"] = scope }, [],
                    scope, [], assignment.StageExecutionId, receipt, null, key + ":accepted"), token);
                accepted = stored.Payload;
            }
            catch (PlatformCapabilityException error) when (error.Code == PlatformCapabilityErrorCode.Conflict)
            {
                accepted = await Load(section, token);
            }
            if (JsonSerializer.Serialize(accepted, Json) != JsonSerializer.Serialize(receipt, Json))
                throw new InvalidOperationException("A concurrent review retained different file evidence; resume from the accepted ledger.");
        }
        var candidateFingerprint = BranchDiffTechnicalReview.Hash(JsonSerializer.Serialize(new { scope, assignment.AssignmentRevision,
            candidate.PublicationId, candidate.CandidateCommitSha, candidate.PatchDigest, candidate.ChangedFiles,
            candidate.RemovedFiles, candidate.ImplementationEvidence }, Json));
        var historyKey = $"task-review-open:{item.Id:N}:{scope}";
        AgentOperatingState<BranchDiffTechnicalReview.ReviewHistory>? history = null;
        async Task<BranchDiffTechnicalReview.ReviewHistory?> ReadHistory(CancellationToken token)
        {
            history = await context.Platform.ReadOperatingStateAsync<BranchDiffTechnicalReview.ReviewHistory>(historyKey, token);
            if (history is not null && (history.SchemaId != BranchDiffTechnicalReview.Policy || history.SchemaVersion != 1 ||
                history.DecisionFingerprint != scope))
                throw new InvalidOperationException("Earlier findings belong to a different review scope.");
            return history?.Payload;
        }
        var result = await BranchDiffTechnicalReview.ExecuteAsync(candidate, item.Planning,
            (read, token) => context.Platform.SourceControl.ReviewMergeAsync(new(item.Id, assignment.AssignmentRevision,
                $"task-review:{assignment.StageExecutionId:N}:{assignment.AttemptId:N}:{read.Offset}")
                { IncludePatch = false, DiffRead = read }, token), client, ct, Load, Save, ReadHistory, candidateFingerprint);
        var latest = await context.Platform.SourceControl.ReviewMergeAsync(new(item.Id, assignment.AssignmentRevision,
            $"task-review:{assignment.AttemptId:N}:final") { IncludePatch = false }, ct);
        var current = await context.Platform.Work.ReadItemAsync(new(assignment.BoardId, item.Id), ct);
        if (latest.PublicationId != candidate.PublicationId || latest.CandidateCommitSha != candidate.CandidateCommitSha ||
            latest.TargetCommitSha != candidate.TargetCommitSha || latest.PatchDigest != candidate.PatchDigest ||
            latest.RepositoryId != candidate.RepositoryId || latest.TargetBranch != candidate.TargetBranch ||
            !latest.ImplementationEvidence.SequenceEqual(candidate.ImplementationEvidence) ||
            !latest.ChangedFiles.SequenceEqual(candidate.ChangedFiles) || !latest.RemovedFiles.SequenceEqual(candidate.RemovedFiles) ||
            current.PlanningRevision != item.PlanningRevision ||
            current.StageAssignments.Where(x => x.StageKey is "development" or "specialist-execution")
                .Any(x => x.AgentInstallationId.ToString() == context.InstallationId) ||
            JsonSerializer.Serialize(current.Planning, Json) != JsonSerializer.Serialize(item.Planning, Json) ||
            JsonSerializer.Serialize(current.Delivery, Json) != JsonSerializer.Serialize(item.Delivery, Json) ||
            current.StageAssignments.Single(x => x.StageKey == "technical-review").AgentInstallationId.ToString() != context.InstallationId)
            throw new InvalidOperationException("The source, target, planning, delivery or independent assignment changed during review.");
        if (result.SourceCommitSha != candidate.CandidateCommitSha || result.TargetCommitSha != candidate.TargetCommitSha ||
            string.IsNullOrWhiteSpace(result.Summary) || result.Findings.Any(string.IsNullOrWhiteSpace) ||
            (result.Approved ? result.Findings.Count != 0 : result.Findings.Count == 0))
            throw new InvalidOperationException("Technical Review must bind both exact commits and return a consistent decision.");
        if (result.SectionCount > 0 && !result.ReusedVerdict)
        {
            var updated = new BranchDiffTechnicalReview.ReviewHistory(candidate.CandidateCommitSha, result.OpenFindings, candidateFingerprint, result);
            // Large verdicts can still be delivered. Keep open findings and re-synthesize on replay.
            if (JsonSerializer.Serialize(updated, Json).Length > AgentOperatingStateValidation.MaximumPayloadCharacters)
                updated = updated with { Verdict = null };
            var revision = history?.Revision ?? 0;
            var fingerprint = BranchDiffTechnicalReview.Hash(JsonSerializer.Serialize(new { scope, revision, updated }, Json));
            var stored = await context.Platform.WriteOperatingStateAsync(new WriteAgentOperatingStateRequest<BranchDiffTechnicalReview.ReviewHistory>(
                historyKey, BranchDiffTechnicalReview.Policy, 1, "Active", new Dictionary<string, string> { ["scope"] = scope },
                [], scope, [], assignment.StageExecutionId, updated, revision, $"review-findings:{fingerprint}"), ct);
            if (JsonSerializer.Serialize(stored.Payload, Json) != JsonSerializer.Serialize(updated, Json))
                throw new InvalidOperationException("Concurrent review changed the open findings; resume against the accepted record.");
        }
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
