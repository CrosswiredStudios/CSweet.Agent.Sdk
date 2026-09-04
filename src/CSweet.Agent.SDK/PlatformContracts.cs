using System.Text.Json;
using System.Text.Json.Serialization;

namespace CSweet.Agent.SDK;

public sealed record BusinessProfileResponse(
    Guid OrganizationId,
    string Name,
    string? BusinessType,
    string? Industry,
    string? Description,
    string? Mission,
    string? LifecycleStage,
    IReadOnlyList<string> TargetCustomers,
    IReadOnlyList<string> Offerings,
    string? RevenueModel,
    IReadOnlyList<string> Jurisdictions,
    string? OperatingStyle,
    IReadOnlyList<string> Constraints,
    IReadOnlyList<string> Tools,
    string? RiskPreference,
    string TimeZone,
    long Revision,
    decimal Completeness,
    IReadOnlyDictionary<string, ProfileFieldProvenance> Provenance);

public sealed record ProfileFieldProvenance(
    string SourceType,
    string? ConversationId,
    string? MessageId,
    DateTimeOffset RecordedAt);

public sealed record ExplicitBusinessProfileUpdateRequest(
    long ExpectedRevision,
    string ConversationId,
    string MessageId,
    string UserId,
    IReadOnlyDictionary<string, JsonElement> Changes,
    string IdempotencyKey);

public sealed record ProposedProfileUpdateRequest(
    string ProfileKind,
    long ExpectedRevision,
    IReadOnlyDictionary<string, JsonElement> Changes,
    string Rationale,
    string IdempotencyKey);

public sealed record MutationResponse(
    bool Applied,
    long Revision,
    Guid? ApprovalId,
    string? Message);

public sealed record OrganizationSnapshotResponse(
    Guid OrganizationId,
    string Status,
    IReadOnlyList<OrganizationPerson> People,
    IReadOnlyList<OrganizationRole> Roles,
    IReadOnlyList<OrganizationObjective> Objectives,
    IReadOnlyList<WorkstreamSummary> Workstreams,
    IReadOnlyList<OrganizationWorker> Workers,
    DateTimeOffset GeneratedAt)
{
    public IReadOnlyList<OperatingSignal> OperatingSignals { get; init; } = [];
    public BudgetPositionSummary? BudgetPosition { get; init; }
}

public sealed record OperatingSignal(
    string Type,
    string Severity,
    string Summary,
    string? ReferenceType = null,
    Guid? ReferenceId = null,
    DateTimeOffset? DueAt = null,
    decimal? FinancialImpact = null,
    string? Currency = null);

public sealed record BudgetPositionSummary(
    string Currency,
    decimal? MostRestrictiveLimit,
    decimal ReservedAmount,
    decimal? AvailableAmount,
    IReadOnlyList<string> Constraints);

public sealed record OrganizationPerson(
    Guid Id,
    string DisplayName,
    string EmployeeType,
    Guid? RoleId,
    Guid? ReportsToId,
    Guid? AgentInstallationId,
    bool IsActive);

public sealed record OrganizationRole(Guid Id, string Name, string Description, string ResponsibilitiesJson);
public sealed record OrganizationObjective(Guid Id, string Title, string Description, string Status, DateTimeOffset? TargetDate);
public sealed record OrganizationWorker(Guid Id, string Name, string WorkerType, IReadOnlyList<string> Capabilities, bool IsEnabled);
public sealed record WorkstreamSummary(
    Guid Id,
    string Name,
    string Outcome,
    string Status,
    string LifecycleStage,
    Guid? AccountableManagerOrganizationUserId,
    DateTimeOffset? TargetDate,
    decimal? BudgetAmount,
    string? BudgetCurrency);

public sealed record BusinessPatternSearchRequest(
    string? BusinessType,
    string? LifecycleStage,
    IReadOnlyList<string>? Jurisdictions = null,
    int MaximumResults = 5);

public sealed record BusinessPatternSearchResponse(
    IReadOnlyList<BusinessPatternMatch> Matches,
    bool ResearchFallbackRecommended,
    string? UnavailableReason);

public sealed record BusinessPatternMatch(
    string PatternId,
    string Version,
    string Name,
    string LifecycleStage,
    IReadOnlyList<PatternWorkstream> Workstreams,
    IReadOnlyList<string> CommonRisks,
    IReadOnlyList<string> FinancialConsiderations,
    string Provenance,
    DateTimeOffset ReviewedAt,
    decimal MatchScore);

public sealed record PatternWorkstream(
    string Name,
    string Outcome,
    string ManagerTitle,
    IReadOnlyList<string> RequiredCapabilities,
    IReadOnlyList<string> SuggestedRoles,
    IReadOnlyList<string> ReviewRequirements);

public sealed record WorkstreamPlanProposalRequest(
    string Name,
    string Outcome,
    IReadOnlyList<string> SuccessCriteria,
    string LifecycleStage,
    string ManagerTitle,
    IReadOnlyList<string> RequiredCapabilities,
    Guid? StrategicObjectiveId,
    DateTimeOffset? TargetDate,
    decimal? ProposedBudgetAmount,
    string? ProposedBudgetCurrency,
    string Rationale,
    string IdempotencyKey);

public sealed record WorkforceSearchRequest(
    IReadOnlyList<string> RequiredCapabilities,
    IReadOnlyList<string>? RequiredCredentials,
    DateTimeOffset? NeededBy,
    decimal? MaximumBudget,
    string? Currency,
    bool HumanRequired,
    string? WorkstreamId,
    int MaximumResults = 10);

public sealed record WorkforceSearchResponse(
    IReadOnlyList<WorkforceCandidate> Candidates,
    IReadOnlyList<RejectedWorkforceCandidate> Rejected,
    bool MarketplaceAvailable,
    string? UnavailableReason);

public sealed record WorkforceCandidate(
    string CandidateId,
    string Source,
    string ResourceType,
    string Name,
    IReadOnlyList<string> Capabilities,
    IReadOnlyList<string> Credentials,
    decimal? EstimatedCost,
    string? Currency,
    decimal Score,
    string Rationale,
    bool RequiresSeparateApproval)
{
    /// <summary>
    /// Optional installable source for agent candidates. The hiring workflow must preview and pin
    /// the resolved manifest digest before presenting an owner approval.
    /// </summary>
    public string? RepositoryUrl { get; init; }
}

public sealed record RejectedWorkforceCandidate(string CandidateId, string Name, string Source, IReadOnlyList<string> Reasons);

/// <summary>The authoritative source categories used by the unified agent catalog.</summary>
public enum AgentCatalogSource
{
    Installed,
    LocalDirectory,
    FirstPartyCatalog,
    Marketplace
}

/// <summary>The installation state of an agent visible through the catalog.</summary>
public enum AgentAvailabilityState
{
    AvailableToInstall,
    Planned,
    InstalledDisabled,
    InstalledEnabled,
    Unavailable
}

/// <summary>Source-independent agent catalog search criteria.</summary>
public sealed record AvailableAgentSearchQuery(
    string? Role = null,
    string? SearchString = null,
    IReadOnlyList<string>? RequiredCapabilities = null,
    string? Category = null,
    decimal? MaximumPrice = null,
    string? Currency = null,
    string? Sort = null,
    int Limit = 25,
    string? RoleCategoryKey = null,
    IReadOnlyList<string>? PreferredSpecializationKeys = null);

/// <summary>A safe, source-independent agent listing. Local filesystem paths are never returned.</summary>
public sealed record AvailableAgent(
    string AgentReference,
    string? AgentId,
    AgentCatalogSource Source,
    IReadOnlyList<AgentCatalogSource> AlternateSources,
    AgentAvailabilityState Availability,
    Guid? InstallationId,
    string Name,
    string Summary,
    string Publisher,
    string Category,
    IReadOnlyList<string> RoleAliases,
    IReadOnlyList<string> Keywords,
    IReadOnlyList<string> Capabilities,
    decimal? Price,
    string? Currency,
    decimal? Rating,
    int RatingCount,
    string? DocumentationUrl,
    string? RepositoryUrl,
    decimal Score,
    string Trust,
    string? RoleKey = null,
    string? RoleName = null,
    string? LicenseSpdxId = null,
    string? LicenseUrl = null,
    IReadOnlyList<string>? IconUrls = null,
    IReadOnlyList<string>? RoleCategoryKeys = null,
    IReadOnlyList<string>? SpecializationKeys = null);

/// <summary>Health information for one catalog source. A failed source does not fail the aggregate search.</summary>
public sealed record AgentCatalogSourceHealth(
    AgentCatalogSource Source,
    bool Available,
    string? Message = null);

public sealed record AvailableAgentSearchResult(
    IReadOnlyList<AvailableAgent> Agents,
    IReadOnlyList<AgentCatalogSourceHealth> Sources);

public sealed record WorkforcePlanProposalRequest(
    Guid WorkstreamId,
    IReadOnlyList<ProposedStaffingAssignment> Assignments,
    IReadOnlyList<string> Assumptions,
    IReadOnlyList<string> Risks,
    decimal? EstimatedMonthlyCost,
    string? Currency,
    string Rationale,
    string IdempotencyKey);

public sealed record AskUserOption(string Id, string Label, string? Description = null);

public sealed record AskUserRequest(
    Guid ConversationId,
    Guid? ChatTurnId,
    string Prompt,
    IReadOnlyList<AskUserOption> Options,
    string RecommendedOptionId,
    string IdempotencyKey,
    Guid? ConversationMessageId = null);

public sealed record UserQuestionOptionResponse(
    string Id, string Label, string? Description, bool Recommended);

public sealed record UserQuestionResponse(
    Guid Id,
    string Prompt,
    string Status,
    IReadOnlyList<UserQuestionOptionResponse> Options,
    string RecommendedOptionId,
    string? SelectedOptionId,
    string? FreeTextAnswer,
    DateTimeOffset CreatedAt,
    DateTimeOffset? AnsweredAt);

public sealed record UpsertHiringRecommendationRequest(
    string Title,
    string Objective,
    Guid? WorkstreamId,
    IReadOnlyList<string> CandidateReferences,
    string? RecommendedCandidateReference,
    string IdempotencyKey)
{
    public int Priority { get; init; } = 50;
    public string? RoleKey { get; init; }
    public int Headcount { get; init; } = 1;
    public Guid? SourceResourceChangeRequestId { get; init; }
    public Guid? TeamId { get; init; }
}

public sealed record HiringCandidateResponse(
    string CandidateReference, string Source, string DisplayName, string ResourceType,
    IReadOnlyList<string> Capabilities, IReadOnlyList<string> Credentials, decimal FitScore,
    decimal? Price, string? Currency, string Trust, bool Available, string InstallationState,
    IReadOnlyList<string> RequiredGrants, string Rationale);

public sealed record HiringRecommendationResponse(
    Guid Id, Guid? WorkstreamId, string Title, string Objective, string Status,
    string? RecommendedCandidateReference, IReadOnlyList<HiringCandidateResponse> Candidates,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt)
{
    public int Priority { get; init; } = 50;
    public string HiringUrl { get; init; } = string.Empty;
    public string? SuggestedBy { get; init; }
    public string? RoleKey { get; init; }
    public int Headcount { get; init; } = 1;
    public Guid? SourceResourceChangeRequestId { get; init; }
    public Guid? TeamId { get; init; }
    public int FulfilledHeadcount { get; init; }
    public int RemainingHeadcount { get; init; }
}

public sealed record HiringBacklogResponse(IReadOnlyList<HiringRecommendationResponse> Recommendations);

public sealed record ResolveHiringRecommendationRequest(
    Guid RecommendationId,
    Guid ResultOrganizationUserId,
    string IdempotencyKey);

public sealed record WithdrawHiringRecommendationRequest(
    Guid RecommendationId,
    string Reason,
    string IdempotencyKey);

public sealed record ResourceChangeRole(
    string RoleKey,
    string Team,
    string Title,
    string Purpose,
    int Headcount,
    int Priority,
    string Timing,
    IReadOnlyList<string> RequiredCapabilities,
    bool HumanRequired,
    Guid? ReportsToOrganizationUserId,
    string? ReportsToRoleKey)
{
    public Guid? TeamId { get; init; }
    /// <summary>Stable high-level role required to fill this plan slot. RoleKey remains the slot identity.</summary>
    public string RoleCategoryKey { get; init; } = string.Empty;
    /// <summary>Optional strengths preferred for this slot. They never make an otherwise eligible candidate ineligible.</summary>
    public IReadOnlyList<string> PreferredSpecializationKeys { get; init; } = [];
}

public sealed record ResourceChangeEvidence(
    string Kind,
    string SourceRevision,
    string Summary,
    decimal? Value = null,
    string? Unit = null,
    DateTimeOffset? WindowStart = null,
    DateTimeOffset? WindowEnd = null);

public sealed record ResourceChangeProposalRequest(
    Guid ConversationId,
    Guid ChatTurnId,
    string ProductGoal,
    string Rationale,
    long ContextRevision,
    IReadOnlyList<ResourceChangeRole> Roles,
    IReadOnlyList<string> Assumptions,
    IReadOnlyList<string> Constraints,
    Guid? SupersedesRequestId,
    string IdempotencyKey)
{
    public string? TeamKey { get; init; }
    public string? TeamName { get; init; }
    public string? TeamDescription { get; init; }
    public Guid? TeamId { get; init; }
    public Guid? WorkstreamId { get; init; }
    public long? ExpectedTeamRevision { get; init; }
    public IReadOnlyList<ResourceChangeEvidence> Evidence { get; init; } = [];
    public IReadOnlyList<string> AlternativesConsidered { get; init; } = [];
    public string? ExpectedEffect { get; init; }
}

public sealed record ResourceChangeRoleDelta(
    string ChangeKind,
    ResourceChangeRole Role,
    ResourceChangeRole? PreviousRole);

public sealed record ResourceChangeRequestResponse(
    Guid Id,
    Guid OrganizationId,
    Guid RequesterOrganizationUserId,
    Guid RequesterInstallationId,
    Guid ManagerOrganizationUserId,
    Guid ConversationId,
    Guid ChatTurnId,
    string ProductGoal,
    string Rationale,
    long ContextRevision,
    IReadOnlyList<ResourceChangeRole> Roles,
    IReadOnlyList<ResourceChangeRoleDelta> Deltas,
    IReadOnlyList<string> Assumptions,
    IReadOnlyList<string> Constraints,
    Guid? SupersedesRequestId,
    string Status,
    string DeliveryStatus,
    string? DecisionComment,
    DateTimeOffset CreatedAt,
    DateTimeOffset? DecidedAt)
{
    public Guid? TeamId { get; init; }
    public string? TeamKey { get; init; }
    public string? TeamName { get; init; }
    public string? TeamDescription { get; init; }
    public Guid? WorkstreamId { get; init; }
    public long? ExpectedTeamRevision { get; init; }
    public IReadOnlyList<ResourceChangeEvidence> Evidence { get; init; } = [];
    public IReadOnlyList<string> AlternativesConsidered { get; init; } = [];
    public string? ExpectedEffect { get; init; }
    public Guid? DecidedByOrganizationUserId { get; init; }
}

public sealed record ResourceChangeReadRequest(
    Guid? RequestId = null,
    IReadOnlyList<string>? Statuses = null);

public sealed record ResourceChangeReadResponse(
    IReadOnlyList<ResourceChangeRequestResponse> Requests);

public static class ResourceChangeDecisionKinds
{
    public const string Approve = "Approve";
    public const string RequestRevision = "RequestRevision";
    public const string Reject = "Reject";
}

public sealed record ResourceChangeDecisionRequest(
    Guid RequestId,
    string Decision,
    string? Comment,
    string IdempotencyKey);

public sealed record ResourceChangeDecisionEvent(
    Guid RequestId,
    Guid OrganizationId,
    Guid RequesterOrganizationUserId,
    Guid ManagerOrganizationUserId,
    string Status,
    DateTimeOffset OccurredAt)
{
    public Guid? TeamId { get; init; }
    public Guid? WorkstreamId { get; init; }
    public Guid? DecidedByOrganizationUserId { get; init; }
}

public sealed record EmployeeHiredEvent(
    Guid OrganizationId,
    Guid OrganizationUserId,
    string EmployeeType,
    Guid? RoleId,
    string? RoleTitle,
    Guid? AgentInstallationId,
    Guid? WorkerId,
    Guid? ReportsToOrganizationUserId,
    Guid? HiringOrganizationUserId,
    string Source,
    DateTimeOffset OccurredAt);

public sealed record HiringRecommendationFulfilledEvent(
    Guid OrganizationId,
    Guid RecommendationId,
    Guid? SourceResourceChangeRequestId,
    Guid RequestingInstallationId,
    string? RoleKey,
    string RoleTitle,
    Guid? TeamId,
    Guid? WorkstreamId,
    int RequestedHeadcount,
    int FulfilledHeadcount,
    IReadOnlyList<Guid> ResultOrganizationUserIds,
    DateTimeOffset OccurredAt);

public sealed record SuggestUserActionRequest(
    Guid? MessageId,
    Guid? ChatTurnId,
    string WorkflowType,
    string Label,
    string? Description,
    JsonElement Parameters,
    string IdempotencyKey);

public sealed record SuggestedUserActionResponse(
    Guid Id,
    string WorkflowType,
    string Label,
    string? Description,
    string NavigationUri,
    string Status,
    DateTimeOffset CreatedAt);

public sealed record StageHiringWorkflowRequest(
    Guid RecommendationId,
    string CandidateReference,
    string RoleTitle,
    Guid? ReportsToOrganizationUserId,
    IReadOnlyList<string>? RequiredGrants,
    string IdempotencyKey)
{
    public Guid? ConversationId { get; init; }
    public Guid? ChatTurnId { get; init; }
}

public sealed record HiringWorkflowResponse(
    Guid Id, Guid RecommendationId, string CandidateReference, string RoleTitle,
    string Status, string Message, DateTimeOffset CreatedAt, Guid? ResultOrganizationUserId = null);

public static class HiringWorkflowDecisionKinds
{
    public const string Approve = "Approve";
    public const string Reject = "Reject";
}

public sealed record DecideHiringWorkflowRequest(
    string Decision,
    string? Comment,
    string IdempotencyKey)
{
    public IReadOnlyDictionary<string, JsonElement> ConfigurationSettings { get; init; } =
        new Dictionary<string, JsonElement>(StringComparer.Ordinal);
}

public sealed record HiringWorkflowApprovalResponse(
    Guid Id,
    string RoleTitle,
    string CandidateReference,
    string CandidateName,
    string CandidateSource,
    string EmployeeDisplayName,
    Guid? ReportsToOrganizationUserId,
    string? ReportsToDisplayName,
    string Status,
    string InstallationConsequence,
    IReadOnlyList<string> RequestedCapabilities,
    IReadOnlyList<string> Subscriptions,
    IReadOnlyList<string> NetworkAccess,
    IReadOnlyList<AgentConfigurationField> ConfigurationFields,
    DateTimeOffset CreatedAt,
    DateTimeOffset? SubmittedAt,
    DateTimeOffset? DecidedAt,
    string? DecisionComment);

public sealed record ProposedStaffingAssignment(
    string PositionKey,
    string Title,
    string CandidateId,
    string CandidateSource,
    decimal Allocation,
    IReadOnlyList<string> RequiredCapabilities);

public sealed record FinancialOperatingProfileResponse(
    Guid OrganizationId,
    string BaseCurrency,
    decimal? RevenueTarget,
    decimal? ProfitTarget,
    decimal? OwnerCompensationTarget,
    decimal? MinimumRunwayMonths,
    decimal? MaximumMonthlyWorkforceSpend,
    decimal? PerEngagementCap,
    int? MaximumConcurrentHires,
    string RoutingPreference,
    long Revision);

public sealed record FinancialProfileProposalRequest(
    long ExpectedRevision,
    IReadOnlyDictionary<string, JsonElement> Changes,
    string Reason,
    string IdempotencyKey);

public sealed record BudgetEvaluationRequest(
    string ScopeType,
    Guid? ScopeId,
    decimal Amount,
    string Currency,
    string Purpose,
    bool Reserve,
    string IdempotencyKey);

public sealed record BudgetEvaluationResponse(
    bool Allowed,
    decimal? AvailableAmount,
    string Currency,
    Guid? ReservationId,
    IReadOnlyList<string> Reasons);

public sealed record ApprovalProposalRequest(
    string ActionType,
    string Summary,
    string PayloadJson,
    string RiskClass,
    string IdempotencyKey);

public sealed record ApprovalProposalResponse(Guid ApprovalId, string Status, DateTimeOffset CreatedAt);

public sealed record ManagementCycleResponse(
    Guid? CycleId,
    string TimeZone,
    string DailyCheckInLocalTime,
    string DailyDueLocalTime,
    string WeeklyReviewDay,
    string WeeklyReviewLocalTime,
    string QuietHoursStart,
    string QuietHoursEnd,
    DateTimeOffset? NextReviewAt)
{
    public ExecutiveBriefingScheduleResponse? ExecutiveBriefing { get; init; }
}

public sealed record ExecutiveBriefingScheduleResponse(
    bool IsEnabled,
    bool StartupEnabled,
    string Cadence,
    string WeeklyDay,
    string LocalTime,
    DateTimeOffset? NextBriefingAt);

public sealed record ManagementCheckInRequest(
    Guid CycleId,
    string CheckInType,
    DateTimeOffset PeriodStart,
    DateTimeOffset PeriodEnd,
    IReadOnlyList<Guid> WorkstreamIds,
    IReadOnlyList<string> Topics,
    DateTimeOffset DueAt)
{
    public Guid? RequestId { get; init; }
}

public sealed record ManagementStatusReport(
    Guid CycleId,
    string Summary,
    IReadOnlyList<string> CompletedOutcomes,
    IReadOnlyList<string> InProgress,
    IReadOnlyList<string> Blockers,
    IReadOnlyList<string> Risks,
    IReadOnlyList<ResourceNeedReport> ResourceNeeds,
    IReadOnlyList<string> DecisionsNeeded,
    IReadOnlyList<string> Assumptions,
    decimal Confidence,
    DateTimeOffset ReportedAt)
{
    public Guid? WorkstreamId { get; init; }
    public Guid? RequestId { get; init; }
    public string? Markdown { get; init; }
    public IReadOnlyList<string> ImmediateActions { get; init; } = [];
    public IReadOnlyList<string> ConversationTopics { get; init; } = [];
    public string Severity { get; init; } = "Important";
    public Guid? ReporterOrganizationUserId { get; init; }
    public string? ReporterDisplayName { get; init; }
    public string? ReporterRole { get; init; }
}

public sealed record ResourceNeedReport(
    string Capability,
    string BusinessOutcome,
    string Urgency,
    string Evidence,
    string ConsequenceIfUnfilled,
    decimal? EstimatedCost,
    string? Currency);

public sealed record ManagementReviewDueEvent(
    Guid CycleId,
    string ReviewType,
    DateTimeOffset PeriodStart,
    DateTimeOffset PeriodEnd,
    DateTimeOffset DueAt,
    string TimeZone)
{
    public Guid? RequestId { get; init; }
}

[JsonConverter(typeof(JsonStringEnumConverter<PlatformCapabilityErrorCode>))]
public enum PlatformCapabilityErrorCode
{
    Unknown,
    Denied,
    Unavailable,
    NotFound,
    Conflict,
    ValidationFailed,
    ApprovalRequired,
    BudgetExceeded
}

public sealed record PlatformCapabilityError(PlatformCapabilityErrorCode Code, string Message);

/// <summary>Implementation-neutral routing seam for alternative workforce planners.</summary>
public interface IWorkforceRouter
{
    Task<WorkforceSearchResponse> SearchAsync(WorkforceSearchRequest request, CancellationToken cancellationToken = default);
}

/// <summary>Adapter boundary for local catalogs, marketplaces, and verified human providers.</summary>
public interface IWorkforceCatalogProvider
{
    string ProviderKey { get; }
    WorkforceCatalogKind CatalogKind { get; }
    Task<WorkforceSearchResponse> SearchAsync(WorkforceSearchRequest request, CancellationToken cancellationToken = default);
}

public enum WorkforceCatalogKind
{
    SuggestedAgent,
    DigitalMarketplace,
    HybridMarketplace,
    HumanMarketplace
}

/// <summary>Adapter boundary for curated and authorized plugin-provided operating pattern catalogs.</summary>
public interface IBusinessPatternProvider
{
    string ProviderKey { get; }
    Task<BusinessPatternSearchResponse> SearchAsync(BusinessPatternSearchRequest request, CancellationToken cancellationToken = default);
}
