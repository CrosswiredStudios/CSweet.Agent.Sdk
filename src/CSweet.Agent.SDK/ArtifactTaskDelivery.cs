using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CSweet.WorkManagement.Contracts;
using Microsoft.Extensions.AI;

namespace CSweet.Agent.SDK;

/// <summary>Repository-free task delivery records the exact revision for independent QA and rework.</summary>
public static class ArtifactTaskDelivery
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public static async Task<AgentWorkResult> ExecuteAsync(WorkExecutionAssignment assignment, WorkItem item,
        AgentRuntimeContext context, IChatClient client, CancellationToken ct)
    {
        if (item.Delivery is not { DeliveryKind: "Artifact", RepositoryId: var repository } || repository != Guid.Empty ||
            item.Delivery.DeliveryPlanId is null || item.Planning is null || assignment.StageKey != "development")
            throw new InvalidOperationException("An authorized artifact task with accepted planning is required.");
        var author = item.StageAssignments.Single(x => x.StageKey == assignment.StageKey);
        if (author.AgentInstallationId.ToString() != context.InstallationId)
            throw new UnauthorizedAccessException("Only the exact assigned author may deliver this revision.");
        var input = assignment.Input.Deserialize<WorkExecutionInputV1>(Json) ?? throw new InvalidOperationException("The canonical assignment input is missing.");
        var sources = await DocumentEvidence.ReadAsync(item.Planning, context, ct);
        var prior = assignment.PriorOutcomes.LastOrDefault(o => WorkArtifactDelivery.TryRead(o.Output, out _));
        Guid? retained = prior is not null && WorkArtifactDelivery.TryRead(prior.Output, out var previous) ? previous.ArtifactId : null;
        string? retainedContent = null;
        if (retained.HasValue)
        {
            var existing = await context.Platform.Artifacts.GetAsync(retained.Value, ct);
            retainedContent = existing.Revisions.Single(r => r.Id == existing.LatestRevisionId).Content;
        }
        var draft = await DocumentWork.AuthorAsync(assignment, item.Planning, sources, context, client,
            "Deliver a substantive Markdown document satisfying the assigned outcomes and acceptance criteria.", null, ct,
            retainedContent: retainedContent);
        if (draft.Blocker is not null) return DocumentWork.Blocked(assignment, draft.Blocker);
        if (string.IsNullOrWhiteSpace(draft.Content) || draft.Content.Length < 200)
            throw new InvalidOperationException("The artifact task returned no substantive deliverable.");
        var (document, revision) = await DocumentWork.PublishAsync(assignment, item.Title, "software.task-delivery.v1", draft,
            input.WorkstreamId, input.TeamId, context, ct, retained);
        var digest = revision.ContentSha256;
        return AgentWorkResult.Success(new WorkExecutionOutcomeV1(assignment.StageExecutionId, assignment.AttemptId,
            WorkExecutionDispositions.Completed, "artifact-delivered", "Delivered exact artifact revision for independent QA.",
            new WorkArtifactDelivery(document.Id, revision.Id, digest).ToOutput(),
            [new("artifact-revision", revision.Id.ToString("D"), digest)], []));
    }
}
