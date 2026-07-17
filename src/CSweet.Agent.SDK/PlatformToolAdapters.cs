using Microsoft.Extensions.AI;

namespace CSweet.Agent.SDK;

/// <summary>Microsoft Agent Framework tools backed by the broker-governed typed client.</summary>
public static class PlatformToolAdapters
{
    public static IReadOnlyList<AITool> Create(PlatformCapabilityClient platform) =>
    [
        AIFunctionFactory.Create(
            (CancellationToken cancellationToken) => platform.ReadBusinessProfileAsync(cancellationToken),
            "read_business_profile", "Read the authoritative business profile."),
        AIFunctionFactory.Create(
            (ExplicitBusinessProfileUpdateRequest request, CancellationToken cancellationToken) => platform.UpdateExplicitBusinessProfileAsync(request, cancellationToken),
            "update_explicit_business_profile", "Save owner-stated low-risk facts with verified message provenance."),
        AIFunctionFactory.Create(
            (ProposedProfileUpdateRequest request, CancellationToken cancellationToken) => platform.ProposeBusinessProfileUpdateAsync(request, cancellationToken),
            "propose_business_profile_update", "Propose inferred or sensitive business-profile changes for approval."),
        AIFunctionFactory.Create(
            (CancellationToken cancellationToken) => platform.ReadOrganizationSnapshotAsync(cancellationToken),
            "read_organization_snapshot", "Read reporting lines, workstreams, staff, and installed agents."),
        AIFunctionFactory.Create(
            (BusinessPatternSearchRequest request, CancellationToken cancellationToken) => platform.SearchBusinessPatternsAsync(request, cancellationToken),
            "search_business_patterns", "Find stage-appropriate operating patterns."),
        AIFunctionFactory.Create(
            (WorkforceSearchRequest request, CancellationToken cancellationToken) => platform.SearchWorkforceAsync(request, cancellationToken),
            "search_workforce", "Search connected workforce sources in platform policy order."),
        AIFunctionFactory.Create(
            (WorkstreamPlanProposalRequest request, CancellationToken cancellationToken) => platform.ProposeWorkstreamAsync(request, cancellationToken),
            "propose_workstream_plan", "Propose a workstream with one accountable manager position."),
        AIFunctionFactory.Create(
            (WorkforcePlanProposalRequest request, CancellationToken cancellationToken) => platform.ProposeWorkforcePlanAsync(request, cancellationToken),
            "propose_workforce_plan", "Propose a workforce plan without installing or contacting anyone."),
        AIFunctionFactory.Create(
            (CancellationToken cancellationToken) => platform.ReadFinanceProfileAsync(cancellationToken),
            "read_finance_profile", "Read financial goals and hard workforce controls."),
        AIFunctionFactory.Create(
            (FinancialProfileProposalRequest request, CancellationToken cancellationToken) => platform.ProposeFinanceProfileUpdateAsync(request, cancellationToken),
            "propose_finance_profile_update", "Propose financial goals or controls for owner approval."),
        AIFunctionFactory.Create(
            (BudgetEvaluationRequest request, CancellationToken cancellationToken) => platform.EvaluateBudgetAsync(request, cancellationToken),
            "evaluate_budget", "Evaluate or reserve budget in the organization base currency."),
        AIFunctionFactory.Create(
            (ApprovalProposalRequest request, CancellationToken cancellationToken) => platform.ProposeApprovalAsync(request, cancellationToken),
            "propose_approval", "Create a separately gated action proposal."),
        AIFunctionFactory.Create(
            (CancellationToken cancellationToken) => platform.ReadManagementCycleAsync(cancellationToken),
            "read_management_cycle", "Read the durable management cadence and quiet hours.")
    ];
}
