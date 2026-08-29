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
    public const string WorkstreamRead = CapabilityNames.Platform.WorkstreamRead;
    public const string WorkstreamPlanProposeV2 = CapabilityNames.Platform.WorkstreamPlanProposeV2;
    public const string WorkstreamChangePropose = CapabilityNames.Platform.WorkstreamChangePropose;
    public const string WorkstreamGateRead = CapabilityNames.Platform.WorkstreamGateRead;
    public const string WorkstreamGateSubmit = CapabilityNames.Platform.WorkstreamGateSubmit;
    public const string WorkstreamGateDecide = CapabilityNames.Platform.WorkstreamGateDecide;
    public const string PortfolioRead = CapabilityNames.Platform.PortfolioRead;
    public const string TeamRosterReadV2 = CapabilityNames.Platform.TeamRosterReadV2;
    public const string DecisionRequest = CapabilityNames.Platform.DecisionRequest;
    public const string DecisionRead = CapabilityNames.Platform.DecisionRead;
    public const string DecisionDecide = CapabilityNames.Platform.DecisionDecide;
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
    public const string AgentOperatingStateRead = CapabilityNames.Platform.AgentOperatingStateRead;
    public const string AgentOperatingStateWrite = CapabilityNames.Platform.AgentOperatingStateWrite;
    public const string StaffingReplenishmentPropose = CapabilityNames.Platform.StaffingReplenishmentPropose;
    public const string StaffingReplenishmentRead = CapabilityNames.Platform.StaffingReplenishmentRead;
    public const string StaffingReplenishmentDecide = CapabilityNames.Platform.StaffingReplenishmentDecide;
    public const string ManagedActionDecide = CapabilityNames.Platform.ManagedActionDecide;
    public const string ArtifactCreate = CapabilityNames.Platform.ArtifactCreate;
    public const string ArtifactRead = CapabilityNames.Platform.ArtifactRead;
    public const string ArtifactRevise = CapabilityNames.Platform.ArtifactRevise;
    public const string ArtifactSubmit = CapabilityNames.Platform.ArtifactSubmit;
    public const string ArtifactDecide = CapabilityNames.Platform.ArtifactDecide;
    public const string ArtifactDecideV2 = CapabilityNames.Platform.ArtifactDecideV2;
    public const string ArtifactRequestAccess = CapabilityNames.Platform.ArtifactRequestAccess;
    public const string ArtifactPackageCreate = CapabilityNames.Platform.ArtifactPackageCreate;
    public const string ArtifactPackageRead = CapabilityNames.Platform.ArtifactPackageRead;
    public const string ArtifactPackageSubmit = CapabilityNames.Platform.ArtifactPackageSubmit;
    public const string ArtifactPackageDecide = CapabilityNames.Platform.ArtifactPackageDecide;
    public const string ToolchainCatalogRead = CapabilityNames.Platform.ToolchainCatalogRead;
    public const string BuildRequest = CapabilityNames.Platform.BuildRequest;
    public const string BuildRead = CapabilityNames.Platform.BuildRead;
    public const string BuildReport = CapabilityNames.Platform.BuildReport;
    public const string ValidationRead = CapabilityNames.Platform.ValidationRead;
    public const string PreviewCreate = CapabilityNames.Platform.PreviewCreate;
    public const string PreviewRead = CapabilityNames.Platform.PreviewRead;
    public const string EvaluationPlan = CapabilityNames.Platform.EvaluationPlan;
    public const string EvaluationRead = CapabilityNames.Platform.EvaluationRead;
    public const string EvaluationReport = CapabilityNames.Platform.EvaluationReport;
    public const string ReleaseReadinessRead = CapabilityNames.Platform.ReleaseReadinessRead;
    public const string ReleaseReadinessSubmit = CapabilityNames.Platform.ReleaseReadinessSubmit;
    public const string PublicationPropose = CapabilityNames.Platform.PublicationPropose;

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        BusinessProfileRead, BusinessProfileUpdateExplicit, BusinessProfileProposeUpdate,
        OrganizationSnapshotRead, BusinessPatternSearch, WorkstreamPlanPropose, WorkstreamRead,
        WorkstreamPlanProposeV2, WorkstreamChangePropose, WorkstreamGateRead, WorkstreamGateSubmit,
        WorkstreamGateDecide, PortfolioRead, TeamRosterReadV2, DecisionRequest, DecisionRead, DecisionDecide,
        WorkforceSearch, AgentCatalogCapabilities.Search, WorkforcePlanPropose, FinanceProfileRead,
        FinanceProfileProposeUpdate, BudgetEvaluate, ApprovalPropose, ManagementCycleRead,
        UserInputRequest, HiringRecommendationList, HiringRecommendationUpsert,
        HiringRecommendationResolve, HiringRecommendationWithdraw, ResourceChangePropose,
        ResourceChangeRead, ResourceChangeDecide, HiringWorkflowStage, UserActionSuggest,
        TeamRosterRead, AgentOperatingStateRead, AgentOperatingStateWrite,
        StaffingReplenishmentPropose, StaffingReplenishmentRead, StaffingReplenishmentDecide,
        ManagedActionDecide, ArtifactCreate, ArtifactRead, ArtifactRevise, ArtifactSubmit,
        ArtifactDecide, ArtifactDecideV2, ArtifactRequestAccess, ArtifactPackageCreate, ArtifactPackageRead,
        ArtifactPackageSubmit, ArtifactPackageDecide, ToolchainCatalogRead, BuildRequest, BuildRead, BuildReport,
        ValidationRead, PreviewCreate, PreviewRead, EvaluationPlan, EvaluationRead, EvaluationReport,
        ReleaseReadinessRead, ReleaseReadinessSubmit, PublicationPropose
    };
}

public static class ArtifactEvents
{
    public const string AccessDecision = "com.csweet.artifact.access-decision.v1";
}

public static class HiringEvents
{
    public const string EmployeeHired = "com.csweet.employee.hired.v1";
    public const string RecommendationFulfilled = "com.csweet.hiring-recommendation.fulfilled.v1";
}

public static class WorkforceEvents
{
    public const string Changed = "com.csweet.workforce.changed.v1";
}

public static class StaffingReplenishmentEvents
{
    public const string Requested = "com.csweet.management.staffing-replenishment.requested.v1";
    public const string Decided = "com.csweet.management.staffing-replenishment.decided.v1";
}

public static class AgentRolePolicyProfiles
{
    public const string Manager = "manager.v1";
    public const string IndividualContributor = "individual-contributor.v1";
    public const string IndependentReviewer = "independent-reviewer.v1";
    public const string ExecutiveAdvisor = "executive-advisor.v1";

    public static IReadOnlySet<string> All { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        Manager, IndividualContributor, IndependentReviewer, ExecutiveAdvisor
    };
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
