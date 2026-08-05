namespace CSweet.Agent.SDK;

/// <summary>Stable capability names implemented by the trusted C-Sweet platform.</summary>
public static class PlatformCapabilities
{
    public const string LlmChatStream = CapabilityNames.Platform.LlmChatStream;
    public const string BusinessProfileRead = CapabilityNames.Platform.BusinessProfileRead;
    public const string BusinessProfileUpdateExplicit = CapabilityNames.Platform.BusinessProfileUpdateExplicit;
    public const string BusinessProfileProposeUpdate = CapabilityNames.Platform.BusinessProfileProposeUpdate;
    public const string OrganizationSnapshotRead = CapabilityNames.Platform.OrganizationSnapshotRead;
    public const string BusinessPatternSearch = CapabilityNames.Platform.BusinessPatternSearch;
    public const string WorkstreamPlanPropose = CapabilityNames.Platform.WorkstreamPlanPropose;
    public const string WorkforceSearch = CapabilityNames.Platform.WorkforceSearch;
    public const string WorkforcePlanPropose = CapabilityNames.Platform.WorkforcePlanPropose;
    public const string FinanceProfileRead = CapabilityNames.Platform.FinanceProfileRead;
    public const string FinanceProfileProposeUpdate = CapabilityNames.Platform.FinanceProfileProposeUpdate;
    public const string BudgetEvaluate = CapabilityNames.Platform.BudgetEvaluate;
    public const string ApprovalPropose = CapabilityNames.Platform.ApprovalPropose;
    public const string ManagementCycleRead = CapabilityNames.Platform.ManagementCycleRead;
    public const string UserInputRequest = CapabilityNames.Platform.UserInputRequest;
    public const string HiringRecommendationList = CapabilityNames.Platform.HiringRecommendationList;
    public const string HiringRecommendationUpsert = CapabilityNames.Platform.HiringRecommendationUpsert;
    public const string HiringRecommendationResolve = CapabilityNames.Platform.HiringRecommendationResolve;
    public const string HiringRecommendationWithdraw = CapabilityNames.Platform.HiringRecommendationWithdraw;
    public const string ResourceChangePropose = CapabilityNames.Platform.ResourceChangePropose;
    public const string ResourceChangeRead = CapabilityNames.Platform.ResourceChangeRead;
    public const string ResourceChangeDecide = CapabilityNames.Platform.ResourceChangeDecide;
    public const string HiringWorkflowStage = CapabilityNames.Platform.HiringWorkflowStage;
    public const string UserActionSuggest = CapabilityNames.Platform.UserActionSuggest;
    public const string TeamRosterRead = CapabilityNames.Platform.TeamRosterRead;
    public const string ManagedActionDecide = CapabilityNames.Platform.ManagedActionDecide;

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        BusinessProfileRead, BusinessProfileUpdateExplicit, BusinessProfileProposeUpdate,
        OrganizationSnapshotRead, BusinessPatternSearch, WorkstreamPlanPropose,
        WorkforceSearch, AgentCatalogCapabilities.Search, WorkforcePlanPropose, FinanceProfileRead,
        FinanceProfileProposeUpdate, BudgetEvaluate, ApprovalPropose, ManagementCycleRead,
        UserInputRequest, HiringRecommendationList, HiringRecommendationUpsert,
        HiringRecommendationResolve, HiringRecommendationWithdraw, ResourceChangePropose,
        ResourceChangeRead, ResourceChangeDecide, HiringWorkflowStage, UserActionSuggest,
        TeamRosterRead, ManagedActionDecide
    };
}

public static class HiringEvents
{
    public const string EmployeeHired = "com.csweet.employee.hired.v1";
    public const string RecommendationFulfilled = "com.csweet.hiring-recommendation.fulfilled.v1";
}

public static class UserActionWorkflows
{
    public const string HiringMarketplaceBrowse = "hiring.marketplace.browse.v1";
}

public static class ManagementCapabilities
{
    public const string CheckIn = CapabilityNames.Management.CheckIn;
    public const string ProductRoleBrief = CapabilityNames.Management.ProductRoleBrief;
    public const string ProductPlanReview = CapabilityNames.Management.ProductPlanReview;
    public const string ProductEscalation = CapabilityNames.Management.ProductEscalation;
}

public static class ManagementEvents
{
    public const string ReviewDue = "com.csweet.management.review.due.v1";
    public const string StatusReported = "com.csweet.management.status.reported.v1";
    public const string ResourceNeedReported = "com.csweet.management.resource-need.reported.v1";
    public const string WorkstreamChanged = "com.csweet.workstream.changed.v1";
    public const string WorkforcePlanDecided = "com.csweet.workforce-plan.decided.v1";
    public const string ResourceChangeRequested = "com.csweet.management.resource-change.requested.v1";
    public const string ResourceChangeDecided = "com.csweet.management.resource-change.decided.v1";
}
