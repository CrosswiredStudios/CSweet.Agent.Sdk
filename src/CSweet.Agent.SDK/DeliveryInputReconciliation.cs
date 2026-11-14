using System.Text.Json;
using CSweet.WorkManagement.Contracts;

namespace CSweet.Agent.SDK;

/// <summary>Propagate an exact accepted planning change without adopting unrelated canonical drift.</summary>
public static class DeliveryInputReconciliation
{
    public static IReadOnlyList<WorkPlanningRevisionChange> Changes(WorkDeliveryPlanResponse plan,
        IReadOnlyDictionary<Guid,long> acceptedRevisions)
    {
        var pins = plan.Scopes.SelectMany(s => s.ChildPlanningRevisions
            .Concat(new Dictionary<Guid,long> { [s.TicketWorkflow!.TicketId] = s.TicketPlanningRevision ?? s.PlanningRevision })
            .Concat(s.ItemId is { } id ? new Dictionary<Guid,long> { [id] = s.PlanningRevision } : new Dictionary<Guid,long>()))
            .GroupBy(p => p.Key).ToDictionary(g => g.Key, g => g.Select(p => p.Value).Distinct().Single());
        return acceptedRevisions.Where(p => pins.TryGetValue(p.Key,out var prior) && p.Value != prior)
            .OrderBy(p => p.Key).Select(p => new WorkPlanningRevisionChange(p.Key,pins[p.Key],p.Value)).ToArray();
    }

    public static async Task<WorkDeliveryPlanResponse> ApplyAsync(WorkDeliveryPlanResponse plan,
        IReadOnlyDictionary<Guid,long> acceptedRevisions, AgentRuntimeContext context, CancellationToken ct,
        IReadOnlyList<WorkDeliveryTaskAssignment>? preservedAssignments = null,
        IReadOnlyList<WorkDeliveryOriginalInput>? preservedInputs = null)
    {
        // An amendment never implicitly resumes an owner pause or creates another plan.
        if (plan.Status != "Active") return plan;
        var changes = Changes(plan,acceptedRevisions);
        if (changes.Count == 0) return plan;
        var key = AgentOperatingStateKey.Create("delivery-input-reconciliation:v1", new { plan.Id, plan.ScopeRevision, changes });
        return await context.Platform.Work.ControlDeliveryPlanAsync(new(plan.Id,plan.Revision,"reconcile-inputs",key,
            "Propagate the exact accepted planning delta; preserve independent acceptance and history.") {
                PlanningChanges=changes,PreservedTaskAssignments=preservedAssignments,PreservedInputs=preservedInputs },ct);
    }
}
