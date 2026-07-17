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
    bool RequiresSeparateApproval);

public sealed record RejectedWorkforceCandidate(string CandidateId, string Name, string Source, IReadOnlyList<string> Reasons);

public sealed record WorkforcePlanProposalRequest(
    Guid WorkstreamId,
    IReadOnlyList<ProposedStaffingAssignment> Assignments,
    IReadOnlyList<string> Assumptions,
    IReadOnlyList<string> Risks,
    decimal? EstimatedMonthlyCost,
    string? Currency,
    string Rationale,
    string IdempotencyKey);

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
    public Guid? RequestId { get; init; }
    public string? Markdown { get; init; }
    public IReadOnlyList<string> ImmediateActions { get; init; } = [];
    public IReadOnlyList<string> ConversationTopics { get; init; } = [];
    public string Severity { get; init; } = "Important";
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
