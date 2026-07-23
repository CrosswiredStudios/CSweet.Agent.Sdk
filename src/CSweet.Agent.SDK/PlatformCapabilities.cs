namespace CSweet.Agent.SDK;

/// <summary>Stable broker capability names implemented by the trusted C-Sweet platform.</summary>
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
    public const string HiringWorkflowStage = CapabilityNames.Platform.HiringWorkflowStage;

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        BusinessProfileRead, BusinessProfileUpdateExplicit, BusinessProfileProposeUpdate,
        OrganizationSnapshotRead, BusinessPatternSearch, WorkstreamPlanPropose,
        WorkforceSearch, WorkforcePlanPropose, FinanceProfileRead,
        FinanceProfileProposeUpdate, BudgetEvaluate, ApprovalPropose, ManagementCycleRead,
        UserInputRequest, HiringRecommendationList, HiringRecommendationUpsert, HiringWorkflowStage
    };

    /// <summary>
    /// Safe platform tools exposed to every authenticated, active installation without a
    /// manifest-requested capability grant. The broker remains authoritative.
    /// </summary>
    public static readonly IReadOnlySet<string> Global = new HashSet<string>(StringComparer.Ordinal)
    {
        UserInputRequest
    };
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
}
