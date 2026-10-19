using CSweet.WorkManagement.Contracts;
using System.Security.Cryptography;
using System.Text;

namespace CSweet.Agent.SDK;

/// <summary>Publishes an agent-authored workflow as canonical tickets. This helper does not insert review steps.</summary>
public static class TicketWorkflowPlanning
{
    public static async Task<WorkDeliveryPlanResponse> ConfigureAsync(ConfigureWorkDeliveryPlanRequest request,
        IReadOnlyList<WorkItem> items, AgentRuntimeContext context, CancellationToken ct)
    {
        var tickets = new Dictionary<(string Scope, Guid? Item), WorkItem>();
        var scopes = request.Assignments;
        WorkItemPlanningSpecification Planning(WorkDeliveryScopeAssignment scope) => scope.Scope == WorkExecutionScopes.Release
            ? request.ReleasePlanning ?? new(items.Where(i => request.EpicItemIds.Contains(i.Id)).SelectMany(i => i.Planning!.Requirements).Distinct().ToArray(),
                items.Where(i => request.EpicItemIds.Contains(i.Id)).SelectMany(i => i.Planning!.AcceptanceCriteria).Distinct().ToArray())
            : items.Single(i => i.Id == scope.ItemId).Planning!;
        WorkDeliveryTicketWorkflow Declaration(WorkDeliveryScopeAssignment scope, Guid ticketId, IReadOnlyList<Guid> children)
        {
            var steps = scope.Stages.Select(assignment => new WorkDeliveryTicketStep(assignment.StageKey,
                assignment.StageKey switch { "technical-review" => "source-review", "manager-review" => "acceptance",
                    "source-preparation" or "collect-evidence" => "completion", "quality" => "quality", "build-readiness" => "build-readiness", _ => "review" },
                assignment, "Perform this ticket's assigned step against its exact candidate, declared requirements and acceptance criteria. Retain real findings and approved deferrals.")).ToArray();
            return new(ticketId, steps) { Scope = scope.Scope, ScopeItemId = scope.ItemId,
                EvidenceItemIds = children.Select(id => tickets.FirstOrDefault(t => t.Key.Item == id).Value?.Id ?? id).ToArray(),
                PromoteOnCompletion = request.Branches.Any(b => b.Scope == scope.Scope && b.ItemId == scope.ItemId && b.SourceBranch != b.TargetBranch),
                CompleteContainerItemIds = scope.Scope == WorkExecutionScopes.Release ? scopes.Where(s => s.ItemId.HasValue).Select(s => s.ItemId!.Value).ToArray() : [],
                FinalReleaseDependencyItemIds = scope.Scope == WorkExecutionScopes.Release ? request.ReleasePlanning?.DependencyItemIds ?? [] : [] };
        }
        foreach (var scope in scopes.OrderBy(s => s.Scope == WorkExecutionScopes.Story ? 0 : s.Scope == WorkExecutionScopes.Epic ? 1 : 2))
        {
            var children = scope.Scope == WorkExecutionScopes.Release ? request.EpicItemIds : items.Where(i => i.ParentItemId == scope.ItemId).Select(i => i.Id).ToArray();
            var predecessors = children.Select(id => tickets.FirstOrDefault(t => t.Key.Item == id).Value?.Id ?? id).ToArray();
            var planning = Planning(scope);
            if (scope.Stages.All(s => s.StageKey is "source-preparation" or "collect-evidence"))
                planning = new(["Prepare the explicitly declared source inputs after predecessor work completes."],
                    ["Predecessor tickets are complete and configured source inputs are prepared; product acceptance is assessed by its separate ticket."]);
            planning = planning with { DependencyItemIds = predecessors, Workflow = Declaration(scope, Guid.Empty, children).Definition() };
            var title = scope.Scope == WorkExecutionScopes.Release ? request.Name + " — acceptance" :
                items.Single(i => i.Id == scope.ItemId).Title + " — " + (scope.Scope == WorkExecutionScopes.Story ? "source preparation" : "evidence preparation");
            var ticket = await context.Platform.Work.CreateItemAsync(new(scope.BoardId, title,
                "Execute the declared steps against the pinned evidence and acceptance criteria. Predecessor tickets determine when this work starts.",
                "Task", "Medium", null, null, null, TicketKey(request.IdempotencyKey, scope.Scope, scope.ItemId))
                { TypeKey = WorkItemTypeKeys.WorkflowTicketV1, Planning = planning, AccountableOrganizationUserId = request.ManagerOrganizationUserId }, ct);
            tickets.Add((scope.Scope, scope.ItemId), ticket);
        }
        var workflows = scopes.Select(scope =>
        {
            var ticket = tickets[(scope.Scope, scope.ItemId)];
            var children = scope.Scope == WorkExecutionScopes.Release ? request.EpicItemIds : items.Where(i => i.ParentItemId == scope.ItemId).Select(i => i.Id).ToArray();
            return Declaration(scope, ticket.Id, children);
        }).ToArray();
        return await context.Platform.Work.ConfigureDeliveryPlanAsync(request with { TicketWorkflows = workflows }, ct);
    }

    internal static string TicketKey(string planKey, string scope, Guid? itemId)
    {
        var original = planKey + ":ticket:" + scope + ":" + itemId;
        // Retain accepted keys for partial/repeated publication. A key above the
        // broker's limit could never have created a ticket; hash its full identity.
        return original.Length <= 160 ? original :
            "workflow-ticket:v1:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(original)));
    }
}
