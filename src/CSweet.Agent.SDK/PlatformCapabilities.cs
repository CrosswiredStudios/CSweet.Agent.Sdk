namespace CSweet.Agent.SDK;

/// <summary>Stable broker capability names implemented by the trusted C-Sweet platform.</summary>
public static class PlatformCapabilities
{
    public const string BusinessProfileRead = "platform.business-profile.read.v1";
    public const string BusinessProfileUpdateExplicit = "platform.business-profile.update-explicit.v1";
    public const string BusinessProfileProposeUpdate = "platform.business-profile.propose-update.v1";
    public const string OrganizationSnapshotRead = "platform.organization.snapshot.read.v1";
    public const string BusinessPatternSearch = "platform.business-pattern.search.v1";
    public const string WorkstreamPlanPropose = "platform.workstream.plan.propose.v1";
    public const string WorkforceSearch = "platform.workforce.search.v1";
    public const string WorkforcePlanPropose = "platform.workforce-plan.propose.v1";
    public const string FinanceProfileRead = "platform.finance-profile.read.v1";
    public const string FinanceProfileProposeUpdate = "platform.finance-profile.propose-update.v1";
    public const string BudgetEvaluate = "platform.budget.evaluate.v1";
    public const string ApprovalPropose = "platform.approval.propose.v1";
    public const string ManagementCycleRead = "platform.management-cycle.read.v1";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        BusinessProfileRead, BusinessProfileUpdateExplicit, BusinessProfileProposeUpdate,
        OrganizationSnapshotRead, BusinessPatternSearch, WorkstreamPlanPropose,
        WorkforceSearch, WorkforcePlanPropose, FinanceProfileRead,
        FinanceProfileProposeUpdate, BudgetEvaluate, ApprovalPropose, ManagementCycleRead
    };
}

public static class ManagementCapabilities
{
    public const string CheckIn = "management.check-in.v1";
}

public static class ManagementEvents
{
    public const string ReviewDue = "com.csweet.management.review.due.v1";
    public const string StatusReported = "com.csweet.management.status.reported.v1";
    public const string ResourceNeedReported = "com.csweet.management.resource-need.reported.v1";
    public const string WorkstreamChanged = "com.csweet.workstream.changed.v1";
    public const string WorkforcePlanDecided = "com.csweet.workforce-plan.decided.v1";
}
