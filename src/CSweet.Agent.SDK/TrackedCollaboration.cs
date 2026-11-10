using System.Text.RegularExpressions;
using CSweet.WorkManagement.Contracts;

namespace CSweet.Agent.SDK;

/// <summary>Stable reason codes for a collaboration that cannot continue on its own.</summary>
public static class CollaborationBlockerReasons
{
    /// <summary>A participant answered Blocked, or the turn limit ended the session without an outcome.</summary>
    public const string ParticipantBlocked = "collaboration.participant-blocked";

    /// <summary>The session failed while running. The platform retries transient failures on its own.</summary>
    public const string Failed = "collaboration.failed";

    /// <summary>The session was cancelled. Retrying the same session does not help.</summary>
    public const string Cancelled = "collaboration.cancelled";
}

/// <summary>
/// A typed description of why a collaboration stopped: a reason code, who owns the next step, what must change,
/// and whether a retry is useful. It is computed from the session; no model is asked.
/// </summary>
public sealed record CollaborationBlocker(
    string ReasonCode,
    Guid? NextStepOwnerOrganizationUserId,
    string? NextStepOwnerDisplayName,
    string WhatMustChange,
    bool RetryUseful)
{
    private const int MaximumReasonLength = 600;

    /// <summary>Returns the blocker for a stopped session, or null while the session is active or completed.</summary>
    public static CollaborationBlocker? FromSession(AgentCoordinationSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        switch (session.Status)
        {
            case AgentCoordinationStatuses.Blocked:
            {
                // The participant who did not report the block owns the next step: they must supply what is missing.
                var blockedTurn = session.Turns.LastOrDefault(x => x.Disposition == AgentCoordinationDispositions.Blocked);
                var owner = blockedTurn is not null && blockedTurn.SpeakerOrganizationUserId == session.Initiator.OrganizationUserId
                    ? session.Target
                    : session.Initiator;
                return new(CollaborationBlockerReasons.ParticipantBlocked, owner.OrganizationUserId, owner.DisplayName,
                    Reason(session.FinalSummary ?? blockedTurn?.Content, "The collaboration stopped without a recorded reason."),
                    true);
            }
            case AgentCoordinationStatuses.Failed:
                return new(CollaborationBlockerReasons.Failed, session.Initiator.OrganizationUserId,
                    session.Initiator.DisplayName,
                    Reason(session.FinalSummary, "The collaboration failed while it was running."), true);
            case AgentCoordinationStatuses.Cancelled:
                return new(CollaborationBlockerReasons.Cancelled, session.Initiator.OrganizationUserId,
                    session.Initiator.DisplayName,
                    Reason(session.FinalSummary, "The collaboration was cancelled. Start a new one if the work is still needed."),
                    false);
            default:
                return null;
        }
    }

    /// <summary>Plain-language text for a blocked card: what must change, who acts next, and whether to retry.</summary>
    public string ToCardReason(string? retryHint = null)
    {
        var owner = string.IsNullOrWhiteSpace(NextStepOwnerDisplayName) ? "" : $" Next step: {NextStepOwnerDisplayName.Trim()}.";
        var retry = RetryUseful
            ? " " + (string.IsNullOrWhiteSpace(retryHint) ? "Move this card back to To Do to retry." : retryHint.Trim())
            : " Retrying will not help until that changes.";
        return $"{WhatMustChange}{owner}{retry} [{ReasonCode}]";
    }

    private static string Reason(string? text, string fallback)
    {
        var flat = Regex.Replace(text ?? "", @"\s+", " ").Trim();
        if (flat.Length == 0) return fallback;
        if (flat.Length > MaximumReasonLength) flat = flat[..MaximumReasonLength].TrimEnd() + "...";
        return flat.EndsWith('.') || flat.EndsWith('!') || flat.EndsWith('?') ? flat : flat + ".";
    }
}

/// <summary>The card that owns one collaboration on its initiator's agenda.</summary>
public sealed record TrackedCollaborationCard(string Title, string Description)
{
    public string Priority { get; init; } = "High";

    /// <summary>The team board the card is shown on. Defaults to the board the collaboration was started on.</summary>
    public Guid? BoardId { get; init; }
    public Guid? TeamId { get; init; }
    public Guid? WorkstreamId { get; init; }
    public Guid? SourceConversationId { get; init; }
    public Guid? SourceMessageId { get; init; }
}

/// <summary>What the owning agent knows about the collaboration's outcome when its card is reviewed.</summary>
public sealed record TrackedCollaborationReview(bool OutcomeReached)
{
    /// <summary>The session the agent currently relies on. A card for an older session completes as replaced.</summary>
    public Guid? CurrentSessionId { get; init; }
    public string OutcomeSummary { get; init; } = "The collaboration reached its outcome.";
    public string WaitingReason { get; init; } = "Waiting for the collaboration to finish.";
    public string SubmittedReason { get; init; } = "The collaboration finished. Waiting for its outcome to take effect.";
    public TimeSpan Recheck { get; init; } = TimeSpan.FromMinutes(10);
    public string? RetryHint { get; init; }
}

/// <summary>
/// Gives a collaboration an owning card, so its state is visible on a board and the platform can resume it
/// through the card. Start the collaboration, call <see cref="QueueCardAsync"/>, and answer the card's callback
/// with <see cref="ReviewAsync"/>.
/// </summary>
public static class TrackedCollaboration
{
    public const string CorrelationPrefix = "tracked-collaboration:";

    public static string Correlation(Guid sessionId) => $"{CorrelationPrefix}{sessionId:N}";

    /// <summary>True when the personal card is the owning card of a collaboration queued by this helper.</summary>
    public static bool IsTrackingCard(PersonalTodoItem item) =>
        item.CorrelationId?.StartsWith(CorrelationPrefix, StringComparison.Ordinal) == true;

    public static bool TryGetSessionId(PersonalTodoItem item, out Guid sessionId)
    {
        sessionId = Guid.Empty;
        return IsTrackingCard(item) &&
               Guid.TryParseExact(item.CorrelationId![CorrelationPrefix.Length..], "N", out sessionId) &&
               sessionId != Guid.Empty;
    }

    /// <summary>
    /// Adds the owning card for a started collaboration. The card is bound to the exact session and, when the
    /// collaboration has one, to its board. Adding is idempotent per session and best effort: a missing
    /// personal to-do grant returns null and leaves the collaboration running.
    /// </summary>
    public static async Task<PersonalTodoItem?> QueueCardAsync(
        AgentRuntimeContext context,
        AgentCoordinationSession session,
        TrackedCollaborationCard card,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(card);
        if (string.IsNullOrWhiteSpace(card.Title))
            throw new ArgumentException("A tracked collaboration card requires a title.", nameof(card));
        var correlation = Correlation(session.Id);
        try
        {
            return await context.Platform.PersonalTodo.AddAsync(new AddPersonalTodoItemRequest(
                card.Title.Trim(), card.Description, card.Priority, null, correlation,
                SourceConversationId: card.SourceConversationId, SourceMessageId: card.SourceMessageId,
                CorrelationId: correlation)
            {
                WorkContext = new PersonalTodoWorkContext(
                    WorkstreamId: card.WorkstreamId,
                    TeamId: card.TeamId,
                    BoardId: card.BoardId ?? session.BoardSource?.BoardId,
                    CoordinationSessionId: session.Id)
            }, cancellationToken);
        }
        catch (PlatformCapabilityException)
        {
            return null;
        }
    }

    /// <summary>
    /// Answers the owning card's callback from the session's current state: completed when the outcome is
    /// reached or the session was replaced, blocked with a typed reason when the session stopped, and
    /// otherwise waiting until the next recheck.
    /// </summary>
    public static async Task<PersonalTodoResult> ReviewAsync(
        AgentRuntimeContext context,
        PersonalTodoItem item,
        TrackedCollaborationReview review,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(review);
        if (!TryGetSessionId(item, out var sessionId))
            return PersonalTodoResult.Blocked("The collaboration reference on this card is invalid.");
        if (review.OutcomeReached) return PersonalTodoResult.Completed(review.OutcomeSummary);
        if (review.CurrentSessionId is { } current && current != sessionId)
            return PersonalTodoResult.Completed("A newer collaboration replaced this one.");
        var session = await context.Platform.Communication.ReadCoordinationAsync(sessionId, cancellationToken);
        if (CollaborationBlocker.FromSession(session) is { } blocker)
            return PersonalTodoResult.Blocked(blocker.ToCardReason(review.RetryHint));
        var recheck = review.Recheck > TimeSpan.Zero ? review.Recheck : TimeSpan.FromMinutes(10);
        return PersonalTodoResult.WaitingUntil(DateTimeOffset.UtcNow.Add(recheck),
            session.Status == AgentCoordinationStatuses.Completed ? review.SubmittedReason : review.WaitingReason);
    }
}
