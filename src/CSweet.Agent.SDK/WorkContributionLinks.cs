using CSweet.WorkManagement.Contracts;

namespace CSweet.Agent.SDK;

/// <summary>An association to retained collaboration evidence, rather than another copy of its content.</summary>
public sealed record WorkContributionLink(Guid WorkstreamId, Guid BoardId, Guid WorkItemId,
    Guid SessionId, Guid AuthorEmployeeId, string ArtifactKey, string ArtifactDigest);

/// <summary>Idempotent, destination-bound projections. Failed display work does not discard the contribution.</summary>
public static class WorkContributionLinks
{
    private const string Prefix = "contribution-link:";
    private const string Schema = "work.contribution-link.v1";

    public static string Key(WorkContributionLink link) => AgentOperatingStateKey.Create(Prefix.TrimEnd(':'), link);
    public static bool IsCommitment(PersonalTodoItem item) => item.CorrelationId?.StartsWith(Prefix, StringComparison.Ordinal) == true;

    /// <summary>Returns false after recording owned repair work if the optional projection failed.</summary>
    public static async Task<bool> AssociateAsync(WorkContributionLink link, AgentRuntimeContext context, CancellationToken ct = default)
    {
        var body = await ReadSourceAsync(link, context, ct);
        try { await PublishAsync(link, body, context, ct); return true; }
        catch (PlatformCapabilityException error) when (!ct.IsCancellationRequested)
        {
            var key = Key(link);
            await DurableWorkHandoff.GetOrCreateAsync(key, Schema, () => Task.FromResult(link), context, ct);
            await context.Platform.PersonalTodo.AddAsync(new AddPersonalTodoItemRequest(
                "Restore the link to retained specialist advice",
                "The contribution is retained in its original collaboration. Restore its work-item reference; substantive assessment can continue from that source. " + error.Message,
                "Low", null, key, CorrelationId: key) {
                    WorkContext = new PersonalTodoWorkContext(WorkstreamId: link.WorkstreamId, BoardId: link.BoardId,
                        WorkItemId: link.WorkItemId, CoordinationSessionId: link.SessionId)
                }, ct);
            return false;
        }
    }

    /// <summary>Shared SDK repair handler; no business decision or execution permission is inferred.</summary>
    public static async Task<PersonalTodoResult?> HandleAsync(PersonalTodoItem item, AgentRuntimeContext context, CancellationToken ct = default)
    {
        if (!IsCommitment(item)) return null;
        var retained = await context.Platform.ReadOperatingStateAsync<WorkContributionLink>(item.CorrelationId!, ct);
        if (retained is null || Key(retained.Payload) != item.CorrelationId ||
            item.WorkContext?.WorkstreamId != retained.Payload.WorkstreamId || item.WorkContext.BoardId != retained.Payload.BoardId ||
            item.WorkContext.WorkItemId != retained.Payload.WorkItemId)
            return PersonalTodoResult.Blocked("The retained contribution association does not match this work. Reconcile its source and destination.");
        try {
            var body = await ReadSourceAsync(retained.Payload, context, ct);
            await PublishAsync(retained.Payload, body, context, ct);
            return PersonalTodoResult.Completed("The work item references the retained contribution; no substantive approval is implied.");
        }
        catch (PlatformCapabilityException error) when (!ct.IsCancellationRequested)
        {
            return error.Retryable == true
                ? PersonalTodoResult.WaitingUntil(DateTimeOffset.UtcNow.AddMinutes(5), error.Message)
                : PersonalTodoResult.Blocked("Contribution-link repair requires correction or current access: " + error.Message);
        }
    }

    private static async Task<string> ReadSourceAsync(WorkContributionLink link, AgentRuntimeContext context, CancellationToken ct)
    {
        var session = await context.Platform.Communication.ReadCoordinationAsync(link.SessionId, ct);
        var latest = session.Turns.Where(t => t.SpeakerOrganizationUserId == link.AuthorEmployeeId)
            .OrderByDescending(t => t.Ordinal).FirstOrDefault();
        if (session.WorkContext?.WorkstreamId != link.WorkstreamId || session.BoardSource?.BoardId != link.BoardId ||
            session.Status is not ("Completed" or "Summarizing") || latest is not { Disposition: "Completed", Artifact: { IsFinalPage: true } artifact } ||
            artifact.Key != link.ArtifactKey || artifact.Digest != link.ArtifactDigest)
            throw new PlatformCapabilityException(CommunicationCapabilities.CoordinationRead, PlatformCapabilityErrorCode.Conflict,
                "The contribution is no longer the expected participant's current completed evidence for this board.");
        var author = session.Target.OrganizationUserId == link.AuthorEmployeeId ? session.Target : session.Initiator;
        return $"{author.DisplayName}'s retained contribution: /organizations/{context.BusinessId}/communications/{session.ConversationId:D} (evidence {link.ArtifactDigest}).";
    }

    private static async Task PublishAsync(WorkContributionLink link, string body, AgentRuntimeContext context, CancellationToken ct)
    {
        await context.Platform.Work.CommentAsync(new(link.BoardId, link.WorkItemId,
            body,
            Key(link)) { Kind = "contribution-reference", CoordinationSessionId = link.SessionId,
                ArtifactDigest = link.ArtifactDigest, CausationId = link.ArtifactKey }, ct);
    }
}
