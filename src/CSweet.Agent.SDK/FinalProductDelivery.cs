using System.Text.Json;
using CSweet.WorkManagement.Contracts;

namespace CSweet.Agent.SDK;

public sealed record FinalProductSpecification(Guid WorkstreamId, Guid BoardId, Guid ManagerId, string ApprovedBasis,
    WorkItemPlanningSpecification Obligations, IReadOnlyList<Guid> AcceptedInputIds,
    IReadOnlyList<WorkDeliveryBranchBinding> Branches, IReadOnlyList<WorkDeliveryTicketStep> Steps)
{
    public WorkApprovedSource? CompletionSource { get; init; }
}

/// <summary>Explicit final-product workflow. Domain agents choose obligations, evidence, handlers and release targets.</summary>
public static class FinalProductDelivery
{
    public sealed record Receipt(Guid PlanId, Guid TicketId);

    public static async Task<WorkDeliveryPlanResponse> PrepareAsync(FinalProductSpecification specification,
        AgentRuntimeContext context, CancellationToken ct)
    {
        if (specification.AcceptedInputIds.Count == 0 || specification.Steps.Count == 0 ||
            specification.Obligations.AcceptanceCriteria.Count == 0 || string.IsNullOrWhiteSpace(specification.ApprovedBasis))
            throw new ArgumentException("Final product preparation needs approved obligations, accepted inputs and explicitly staffed steps.");
        var key = AgentOperatingStateKey.Create("final-product:v1",new { specification.WorkstreamId,specification.ApprovedBasis });
        var receipt = await DurableWorkHandoff.GetOrCreateAsync(key,"work.final-product.v1",async () => {
            var board = await context.Platform.Work.ReadBoardAsync(specification.BoardId,ct);
            if (board.Board.ManagerOrganizationUserId != specification.ManagerId || board.Board.WorkstreamId != specification.WorkstreamId ||
                specification.AcceptedInputIds.Any(id => !board.Items.Any(i=>i.Id==id&&i.Status is "Done" or "Completed")))
                throw new InvalidOperationException("Final preparation requires the current accountable manager and completed canonical evidence inputs.");
            var workflow = new WorkItemWorkflowDefinition(specification.Steps) { Scope=WorkExecutionScopes.Release,ProjectCompletionSource=specification.CompletionSource,
                EvidenceItemIds=specification.AcceptedInputIds,PromoteOnCompletion=specification.Branches.Count>0,
                FinalReleaseDependencyItemIds=specification.Obligations.DependencyItemIds };
            var ticket = await context.Platform.Work.CreateItemAsync(new(specification.BoardId,"Verify final product",
                "Verify the final integrated candidate against approved obligations. Internal acceptance does not authorize public launch.",
                "Task","High",null,null,null,key+":ticket") { TypeKey=WorkItemTypeKeys.WorkflowTicketV1,
                Planning=specification.Obligations with { DependencyItemIds=specification.AcceptedInputIds.Concat(specification.Obligations.DependencyItemIds).Distinct().ToArray(),Workflow=workflow },
                AccountableOrganizationUserId=specification.ManagerId },ct);
            var declaration = new WorkDeliveryTicketWorkflow(ticket.Id,workflow.Steps) { Scope=workflow.Scope,ProjectCompletionSource=workflow.ProjectCompletionSource,
                EvidenceItemIds=workflow.EvidenceItemIds,PromoteOnCompletion=workflow.PromoteOnCompletion,
                FinalReleaseDependencyItemIds=workflow.FinalReleaseDependencyItemIds };
            var plan = await context.Platform.Work.ConfigureDeliveryPlanAsync(new(specification.WorkstreamId,"Final product",specification.ManagerId,
                [],specification.Branches,[],key+":configure") { TicketWorkflows=[declaration] },ct);
            return new Receipt(plan.Id,ticket.Id);
        },context,ct);
        var retained = (await context.Platform.Work.ReadDeliveryPlansAsync(new(specification.WorkstreamId,receipt.PlanId),ct)).Single();
        if (retained.Status == "Draft")
            retained = await context.Platform.Work.ControlDeliveryPlanAsync(new(retained.Id,retained.Revision,"activate",key+":activate"),ct);
        return retained;
    }

    /// <summary>Follow explicit successful correction receipts; cancellation alone never proves acceptance.</summary>
    public static WorkDeliveryPlanResponse? AcceptedSuccessor(WorkDeliveryPlanResponse plan, IReadOnlyList<WorkDeliveryPlanResponse> history)
    {
        var seen = new HashSet<Guid>();
        while (seen.Add(plan.Id))
        {
            if (plan.Status == "Completed" && !plan.Findings.Any(f=>f.Status=="Open")) return plan;
            if (plan.Status != "Cancelled" || plan.Corrections.Count == 0 || plan.Corrections.Any(c=>c.Status!="Completed") || plan.Findings.Any(f=>f.Status=="Open")) return null;
            var ids=plan.Corrections.Select(c=>c.FollowUpPlanId).Distinct().ToArray();
            if (ids.Length!=1 || ids[0] is not { } next || history.SingleOrDefault(p=>p.Id==next) is not { } successor) return null;
            plan=successor;
        }
        return null;
    }
}
