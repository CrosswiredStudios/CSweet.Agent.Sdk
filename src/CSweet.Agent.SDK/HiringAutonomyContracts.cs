namespace CSweet.Agent.SDK;

public static class HiringAutonomyCapabilities
{
    public const string Read = CapabilityNames.Platform.HiringPolicyRead;
    public const string CaptureDecision = CapabilityNames.Platform.HiringPolicyCaptureDecision;
    public const string SelectCandidate = CapabilityNames.Platform.HiringCandidateSelect;
    public const string Submit = CapabilityNames.Platform.HiringDelegationSubmit;
}
public enum HiringSelectionMode { RecommendCandidates, ChooseCandidates, Automatic }
public enum AutomaticHiringLevel { ApprovedPackages, WithinLimits, BroadDelegation }
public enum HiringPublisherPreference { PreferFirstParty, FirstPartyOnly, BestFit }

/// <summary>Owner-controlled hiring authority; conversation memory cannot grant authority.</summary>
public sealed record HiringPolicySettings(
    HiringSelectionMode Mode = HiringSelectionMode.RecommendCandidates,
    AutomaticHiringLevel AutomaticLevel = AutomaticHiringLevel.ApprovedPackages,
    HiringPublisherPreference Publishers = HiringPublisherPreference.PreferFirstParty,
    decimal? MaximumHireCost = null,
    decimal? MaximumTotalCost = null,
    string? Currency = null,
    IReadOnlyList<string>? AllowedCapabilities = null,
    IReadOnlyList<string>? AllowedNetworkAccess = null);
public sealed record HiringPolicyResponse(Guid InstallationId, long Revision, HiringPolicySettings Settings,
    bool SetupComplete, Guid? OwnerId = null, Guid? SourceDecisionId = null, string? Rationale = null, string SetupStage = "mode");
public sealed record CaptureHiringPolicyDecisionRequest(Guid DecisionId, long ExpectedRevision, Guid? AnswerTurnId = null);
public sealed record SelectHiringCandidateRequest(Guid RecommendationId)
{
    public HiringCandidateDecision? Decision { get; init; }
}

/// <summary>The host revalidates the evidence and binds the selected catalog reference under the hiring lock.</summary>
public sealed record HiringCandidateDecision(string Evidence, string CandidateReference, string Rationale,
    string? Question = null, string? AlternativeReference = null, string? AlternativeCondition = null);

public sealed record HiringCandidateBrief(string Evidence, string Title, string Objective, string? ProjectGoal,
    string? RolePurpose, IReadOnlyList<string> Constraints, IReadOnlyList<string> RequiredCapabilities,
    IReadOnlyList<string> PreferredSpecializations, HiringPublisherPreference Publishers,
    IReadOnlyList<AvailableAgent> Candidates, Guid? PlanAuthorId, string? Question, string? Answer,
    string? DispatchKey);

public sealed record HiringCandidateReviewWake(Guid RecommendationId)
{
    public const string EventType = "com.csweet.hiring-candidate.review-requested.v1";
}
public sealed record HiringCandidateSelectionResponse(Guid RecommendationId, HiringSelectionMode Mode,
    AvailableAgent? Candidate, string Rationale, string? Exception = null)
{
    public HiringCandidateBrief? Brief { get; init; }
}
public sealed record SubmitDelegatedHireRequest(Guid RecommendationId);
public sealed record DelegatedHireResponse(Guid RecommendationId, Guid? WorkflowId, string Status, string? Message);

public sealed record UpdateHiringPolicyRequest(HiringPolicySettings Settings, long ExpectedRevision, string Rationale);
