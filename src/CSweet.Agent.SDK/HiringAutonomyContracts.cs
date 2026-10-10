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
public sealed record SelectHiringCandidateRequest(Guid RecommendationId);
public sealed record HiringCandidateSelectionResponse(Guid RecommendationId, HiringSelectionMode Mode,
    AvailableAgent? Candidate, string Rationale, string? Exception = null);
public sealed record SubmitDelegatedHireRequest(Guid RecommendationId);
public sealed record DelegatedHireResponse(Guid RecommendationId, Guid? WorkflowId, string Status, string? Message);

public sealed record UpdateHiringPolicyRequest(HiringPolicySettings Settings, long ExpectedRevision, string Rationale);
