using Microsoft.Extensions.AI;

namespace CSweet.Agent.SDK;

/// <summary>Microsoft Agent Framework tools backed by the broker-governed typed client.</summary>
public static class PlatformToolAdapters
{
    public static IReadOnlyList<AITool> Create(
        PlatformCapabilityClient platform,
        IReadOnlySet<string>? grantedCapabilities = null)
    {
        IReadOnlyList<(string Capability, AITool Tool)> tools =
        [
            (PlatformCapabilities.BusinessProfileRead, AIFunctionFactory.Create(
            (CancellationToken cancellationToken) => platform.ReadBusinessProfileAsync(cancellationToken),
            "read_business_profile", "Read the authoritative business profile.")),
            (PlatformCapabilities.BusinessProfileUpdateExplicit, AIFunctionFactory.Create(
            (ExplicitBusinessProfileUpdateRequest request, CancellationToken cancellationToken) => platform.UpdateExplicitBusinessProfileAsync(request, cancellationToken),
            "update_explicit_business_profile", "Save owner-stated low-risk facts with verified message provenance.")),
            (PlatformCapabilities.BusinessProfileProposeUpdate, AIFunctionFactory.Create(
            (ProposedProfileUpdateRequest request, CancellationToken cancellationToken) => platform.ProposeBusinessProfileUpdateAsync(request, cancellationToken),
            "propose_business_profile_update", "Propose inferred or sensitive business-profile changes for approval.")),
            (PlatformCapabilities.OrganizationSnapshotRead, AIFunctionFactory.Create(
            (CancellationToken cancellationToken) => platform.ReadOrganizationSnapshotAsync(cancellationToken),
            "read_organization_snapshot", "Read reporting lines, workstreams, staff, and installed agents.")),
            (PlatformCapabilities.BusinessPatternSearch, AIFunctionFactory.Create(
            (BusinessPatternSearchRequest request, CancellationToken cancellationToken) => platform.SearchBusinessPatternsAsync(request, cancellationToken),
            "search_business_patterns", "Find stage-appropriate operating patterns.")),
            (PlatformCapabilities.WorkforceSearch, AIFunctionFactory.Create(
            (WorkforceSearchRequest request, CancellationToken cancellationToken) => platform.SearchWorkforceAsync(request, cancellationToken),
            "search_workforce", "Search connected workforce sources in platform policy order.")),
            (PlatformCapabilities.WorkstreamPlanPropose, AIFunctionFactory.Create(
            (WorkstreamPlanProposalRequest request, CancellationToken cancellationToken) => platform.ProposeWorkstreamAsync(request, cancellationToken),
            "propose_workstream_plan", "Propose a workstream with one accountable manager position.")),
            (PlatformCapabilities.WorkforcePlanPropose, AIFunctionFactory.Create(
            (WorkforcePlanProposalRequest request, CancellationToken cancellationToken) => platform.ProposeWorkforcePlanAsync(request, cancellationToken),
            "propose_workforce_plan", "Propose a workforce plan without installing or contacting anyone.")),
            (PlatformCapabilities.FinanceProfileRead, AIFunctionFactory.Create(
            (CancellationToken cancellationToken) => platform.ReadFinanceProfileAsync(cancellationToken),
            "read_finance_profile", "Read financial goals and hard workforce controls.")),
            (PlatformCapabilities.FinanceProfileProposeUpdate, AIFunctionFactory.Create(
            (FinancialProfileProposalRequest request, CancellationToken cancellationToken) => platform.ProposeFinanceProfileUpdateAsync(request, cancellationToken),
            "propose_finance_profile_update", "Propose financial goals or controls for owner approval.")),
            (PlatformCapabilities.BudgetEvaluate, AIFunctionFactory.Create(
            (BudgetEvaluationRequest request, CancellationToken cancellationToken) => platform.EvaluateBudgetAsync(request, cancellationToken),
            "evaluate_budget", "Evaluate or reserve budget in the organization base currency.")),
            (PlatformCapabilities.ApprovalPropose, AIFunctionFactory.Create(
            (ApprovalProposalRequest request, CancellationToken cancellationToken) => platform.ProposeApprovalAsync(request, cancellationToken),
            "propose_approval", "Create a separately gated action proposal.")),
            (PlatformCapabilities.ManagementCycleRead, AIFunctionFactory.Create(
            (CancellationToken cancellationToken) => platform.ReadManagementCycleAsync(cancellationToken),
            "read_management_cycle", "Read the durable management cadence and quiet hours.")),
            (PlatformCapabilities.UserInputRequest, AIFunctionFactory.Create(
            (AskUserRequest request, CancellationToken cancellationToken) => platform.AskUserAsync(request, cancellationToken),
            "ask_user", "Ask one multiple-choice question with two to four mutually exclusive options and one recommendation. The UI adds Something else.")),
            (PlatformCapabilities.HiringRecommendationList, AIFunctionFactory.Create(
            (CancellationToken cancellationToken) => platform.ListHiringRecommendationsAsync(cancellationToken),
            "list_hiring_recommendations", "Read this agent installation's role backlog in priority order.")),
            (PlatformCapabilities.HiringRecommendationUpsert, AIFunctionFactory.Create(
            (UpsertHiringRecommendationRequest request, CancellationToken cancellationToken) => platform.UpsertHiringRecommendationAsync(request, cancellationToken),
            "upsert_hiring_recommendation", "Create or update a prioritized role backlog item. Candidates may be empty until sourcing begins.")),
            (PlatformCapabilities.HiringWorkflowStage, AIFunctionFactory.Create(
            (StageHiringWorkflowRequest request, CancellationToken cancellationToken) => platform.StageHiringWorkflowAsync(request, cancellationToken),
            "stage_hiring_workflow", "Stage a combined install-and-hire workflow for organization-owner approval. This does not install or hire directly."))
        ];

        return tools
            .Where(item => grantedCapabilities is null ||
                           PlatformCapabilities.Global.Contains(item.Capability) ||
                           grantedCapabilities.Contains(item.Capability))
            .Select(item => item.Tool)
            .ToList();
    }
}
