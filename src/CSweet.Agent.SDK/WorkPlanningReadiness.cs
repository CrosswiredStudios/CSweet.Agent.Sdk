using CSweet.WorkManagement.Contracts;

namespace CSweet.Agent.SDK;

/// <summary>Deterministic planning checks, separate from semantic advice and execution acceptance.</summary>
public static class WorkPlanningReadiness
{
    public sealed record Issue(Guid ItemId,string Code,string Message);
    public static IReadOnlyList<Issue> Validate(IEnumerable<WorkItem> items) => items.SelectMany(item =>
    {
        var issues=new List<Issue>();
        if(item.Planning?.Requirements.Count is not >0)
            issues.Add(new(item.Id,"requirements-missing","Current requirements are required."));
        if(item.Planning?.AcceptanceCriteria.Count is not >0)
            issues.Add(new(item.Id,"acceptance-missing","Current acceptance criteria are required."));
        if(item.EstimatePoints is not >0 || string.IsNullOrWhiteSpace(item.EstimateProvenance?.SourceDigest) ||
            item.EstimateProvenance?.PlanningRevision is {} revision && revision!=item.PlanningRevision)
            issues.Add(new(item.Id,"estimate-provenance-missing","A positive current role-owned estimate with source evidence is required."));
        if(!item.StageAssignments.Any(a=>a.StageKey is "development" or "specialist-execution" &&
            a.OrganizationUserId.HasValue && (a.PrincipalKind=="Human" || a.AgentInstallationId.HasValue&&
                !string.IsNullOrWhiteSpace(a.SelectionEvidence?.DecisionFingerprint))))
            issues.Add(new(item.Id,"assignment-evidence-missing","An exact accountable author assignment is required."));
        return issues;
    }).ToArray();
}
