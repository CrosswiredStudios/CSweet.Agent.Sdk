namespace CSweet.Agent.SDK;

/// <summary>Platform-owned focus, coverage and recovery. These capabilities never grant hiring authority.</summary>
public static class ChiefLeadershipCapabilities
{
    public const string Read = CapabilityNames.ChiefLeadership.Read;
    public const string Present = CapabilityNames.ChiefLeadership.Present;
    public const string Capture = CapabilityNames.ChiefLeadership.Capture;
    public const string Assess = CapabilityNames.ChiefLeadership.Assess;
    public const string Control = CapabilityNames.ChiefLeadership.Control;
    public const string Changed = "com.csweet.chief-leadership.changed.v1";
    public static IReadOnlySet<string> All { get; } = new HashSet<string>([Read, Present, Capture, Assess, Control], StringComparer.Ordinal);
}

public sealed record ChiefCoverageDomain(string Id, string Label, string Description, int Priority);
public sealed record PresentChiefFocusRequest(string ProfileKey, int ProfileVersion, Guid ConversationId, Guid MessageId,
    IReadOnlyList<ChiefCoverageDomain> Domains, long ExpectedFocusRevision, string IdempotencyKey,
    Guid? OwnerTurnId = null, string? ExplicitDomainId = null);
/// <summary>DecisionId is absent when a clear authenticated initial owner priority was captured without asking again.</summary>
public sealed record PresentedChiefFocus(Guid? DecisionId, Guid MessageId, long FocusRevision, Guid? AnswerTurnId = null);
public sealed record CaptureChiefFocusRequest(Guid AnswerTurnId, long ExpectedFocusRevision, string IdempotencyKey,
    Guid? DecisionId = null, string? ExplicitDomainId = null);
public sealed record ReadChiefLeadershipRequest(Guid? AfterAssessmentId = null, int PageSize = 50, Guid? AnswerTurnId = null);
public sealed record ChiefFocus(Guid Id, long Revision, string DomainId, string? CustomText, Guid DecisionMakerId,
    Guid ConversationId, Guid SourceTurnId, Guid? SourceDecisionId, DateTimeOffset EffectiveAt, Guid? SupersedesId);
public sealed record ChiefCoverageAssessment(Guid Id, string ProfileKey, int ProfileVersion, string DomainId, string Label,
    string Description, int Priority, string CoverageStatus, string ExecutionStatus, long Revision,
    Guid? AccountableOwnerId, string? CoverageModel, string? Rationale, string? NextAction, Guid? RecommendationId,
    Guid? WorkItemId, Guid? DecisionId, DateTimeOffset? ReviewAt, string? BlockCode, string? InputFingerprint);
public sealed record ChiefCoverageAssignment(Guid OrganizationUserId, Guid? InstallationId, bool Human,
    IReadOnlyList<string> RoleKeys);
public sealed record ChiefLeadershipSnapshot(long FocusRevision, ChiefFocus? Focus,
    IReadOnlyList<ChiefCoverageAssessment> Assessments, IReadOnlyList<ChiefCoverageAssignment> Assignments,
    bool ProactiveEnabled, bool Paused, Guid ResponsibleInstallationId, Guid? NextAssessmentId,
    string InputFingerprint, Guid? AnsweredDecisionId = null, string? ConfigurationBlock = null);

/// <summary>Accepted interpretation contains public evidence references, never model reasoning or authority.</summary>
public sealed record ChiefCoverageResult(string CoverageStatus, string CoverageModel, Guid? AccountableOwnerId,
    string Rationale, string NextAction, IReadOnlyList<Guid> EvidenceOrganizationUserIds,
    string? Question = null, string? RoleKey = null, string? RoleTitle = null, string? RoleObjective = null,
    bool HumanRequired = false, DateTimeOffset? ReviewAt = null);
public sealed record RecordChiefAssessmentRequest(Guid AssessmentId, long ExpectedRevision, long ExpectedFocusRevision,
    string InputFingerprint, ChiefCoverageResult Result, string IdempotencyKey);
public sealed record ControlChiefLeadershipRequest(Guid OwnerTurnId, bool Paused, long ExpectedFocusRevision, string IdempotencyKey);

public sealed class PlatformChiefLeadershipClient(PlatformCapabilityClient platform)
{
    public Task<ChiefLeadershipSnapshot> ReadAsync(ReadChiefLeadershipRequest? request = null, CancellationToken token = default)
    {
        request ??= new();
        if (request.PageSize is < 1 or > 100) throw new ArgumentException("Page size must be between 1 and 100.", nameof(request));
        return platform.InvokeAsync<ReadChiefLeadershipRequest, ChiefLeadershipSnapshot>(ChiefLeadershipCapabilities.Read, request, token);
    }
    public Task<PresentedChiefFocus> PresentAsync(PresentChiefFocusRequest request, CancellationToken token = default) =>
        platform.InvokeAsync<PresentChiefFocusRequest, PresentedChiefFocus>(ChiefLeadershipCapabilities.Present, request, token);
    public Task<ChiefLeadershipSnapshot> CaptureAsync(CaptureChiefFocusRequest request, CancellationToken token = default) =>
        platform.InvokeAsync<CaptureChiefFocusRequest, ChiefLeadershipSnapshot>(ChiefLeadershipCapabilities.Capture, request, token);
    public Task<ChiefCoverageAssessment> AssessAsync(RecordChiefAssessmentRequest request, CancellationToken token = default) =>
        platform.InvokeAsync<RecordChiefAssessmentRequest, ChiefCoverageAssessment>(ChiefLeadershipCapabilities.Assess, request, token);
    public Task<ChiefLeadershipSnapshot> ControlAsync(ControlChiefLeadershipRequest request, CancellationToken token = default) =>
        platform.InvokeAsync<ControlChiefLeadershipRequest, ChiefLeadershipSnapshot>(ChiefLeadershipCapabilities.Control, request, token);
}
