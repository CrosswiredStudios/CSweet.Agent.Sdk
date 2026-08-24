namespace CSweet.Agent.SDK;

public static class StaffingReplenishmentStatuses
{
    public const string Pending = "Pending";
    public const string Approved = "Approved";
    public const string RevisionRequested = "RevisionRequested";
    public const string Rejected = "Rejected";
}

public sealed record StaffingReplenishmentGap(
    string RoleKey,
    string RoleTitle,
    int DesiredHeadcount,
    int EffectiveHeadcount,
    int MissingHeadcount,
    IReadOnlyList<string> EligibilityEvidence);

public sealed record StaffingReplenishmentProposalRequest(
    Guid SourceResourceChangeRequestId,
    Guid TeamId,
    Guid ConversationId,
    IReadOnlyList<StaffingReplenishmentGap> Gaps,
    string OperationalImpact,
    IReadOnlyList<string> InterimControls,
    string DecisionFingerprint,
    string IdempotencyKey);

public sealed record StaffingReplenishmentReadRequest(
    Guid? RequestId = null,
    Guid? SourceResourceChangeRequestId = null,
    IReadOnlyList<string>? Statuses = null);

public sealed record StaffingReplenishmentReadResponse(
    IReadOnlyList<StaffingReplenishmentResponse> Requests);

public sealed record StaffingReplenishmentDecisionRequest(
    Guid RequestId,
    string Decision,
    string? Comment,
    string IdempotencyKey);

public sealed record StaffingReplenishmentResponse(
    Guid Id,
    Guid OrganizationId,
    Guid RequesterOrganizationUserId,
    Guid RequesterInstallationId,
    Guid ManagerOrganizationUserId,
    Guid SourceResourceChangeRequestId,
    Guid TeamId,
    Guid ConversationId,
    IReadOnlyList<StaffingReplenishmentGap> Gaps,
    string OperationalImpact,
    IReadOnlyList<string> InterimControls,
    string DecisionFingerprint,
    string Status,
    string? DecisionComment,
    DateTimeOffset CreatedAt,
    DateTimeOffset? DecidedAt);

public sealed record StaffingReplenishmentDecisionEvent(
    Guid RequestId,
    Guid OrganizationId,
    Guid RequesterOrganizationUserId,
    Guid ManagerOrganizationUserId,
    string Status,
    DateTimeOffset OccurredAt);

public sealed record WorkforceChangedEvent(
    Guid OrganizationId,
    Guid OrganizationUserId,
    string ChangeKind,
    IReadOnlyList<Guid> AffectedTeamIds,
    Guid? PreviousManagerOrganizationUserId,
    Guid? CurrentManagerOrganizationUserId,
    DateTimeOffset OccurredAt);
