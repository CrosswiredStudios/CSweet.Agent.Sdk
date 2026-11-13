using CSweet.WorkManagement.Contracts;
using Microsoft.Extensions.AI;

namespace CSweet.Agent.SDK;

/// <summary>Receipt-driven corrective planning. No model report or conversation is an execution dependency.</summary>
public static partial class DeliveryCorrectionCoordinator
{
    public static Task ReconcileProjectAsync(Guid projectId, AgentRuntimeContext context, CancellationToken ct) =>
        ReconcileProjectCoreAsync(projectId, context, null, ct);

    /// <summary>Continues multi-source planning with a tool-free, evidence-constrained manager decision.
    /// The factory is invoked only when selection is needed; the coordinator disposes the returned client.</summary>
    public static Task ReconcileProjectAsync(Guid projectId, AgentRuntimeContext context,
        Func<IChatClient> createPlanningClient, CancellationToken ct) =>
        ReconcileProjectCoreAsync(projectId, context, createPlanningClient, ct);

    private static async Task ReconcileProjectCoreAsync(Guid projectId, AgentRuntimeContext context,
        Func<IChatClient>? createPlanningClient, CancellationToken ct)
    {
        var plans = await context.Platform.Work.ReadDeliveryPlansAsync(new(projectId, Limit: 100), ct);
        if (!Guid.TryParse(context.Identity?.EmployeeId, out var manager)) return;
        // Bounded discovery on event/reconnect. Current state, not a notification snapshot, grants work.
        var pending = plans.Where(p => p.ManagerOrganizationUserId == manager && p.Status is "Active" or "Paused")
            .SelectMany(p => p.Corrections.Where(c => c.OwnerOrganizationUserId == manager && c.Status != "Completed").Select(c => (Plan: p, Correction: c)))
            .OrderBy(x => x.Correction.FollowUpPlanId.HasValue ? 1 : 0)
            .Take(1).ToArray();
        foreach (var (plan, correction) in pending)
        {
            try { await ReconcileAsync(plan, correction, plans, context, createPlanningClient, ct); }
            catch (PlatformCapabilityException error) when (error.Code is PlatformCapabilityErrorCode.Conflict or PlatformCapabilityErrorCode.Denied or PlatformCapabilityErrorCode.NotFound)
            {
                await RetainWaitAsync(plan, correction, "Corrective planning needs current staffing, scope or grants: " + error.Message, context, ct);
            }
            catch (InvalidOperationException error)
            { await RetainWaitAsync(plan, correction, error.Message, context, ct); }
        }
    }

    private static async Task ReconcileAsync(WorkDeliveryPlanResponse plan, WorkDeliveryCorrection correction,
        IReadOnlyList<WorkDeliveryPlanResponse> plans, AgentRuntimeContext context, Func<IChatClient>? createPlanningClient, CancellationToken ct)
    {
        var work = context.Platform.Work;
        if (correction.FollowUpPlanId is { } existing && plans.SingleOrDefault(p => p.Id == existing) is { } followUp)
        {
            if (followUp.Status == "Completed" || followUp.Status == "Cancelled" && followUp.Corrections.Any(c => c.Status == "Completed"))
            {
                await work.CoordinateDeliveryCorrectionAsync(new(plan.Id, correction.Id, correction.Revision,
                    $"correction-finish:{correction.Id:N}:{correction.Revision}"), ct);
                return;
            }
            if (followUp.Status == "Active")
            {
                if (correction.RemediationTaskIds is { Count: 1 })
                    await EnsureSprintAsync(correction, correction.RemediationTaskIds[0], context, ct);
                return; // Existing engineering/review owns execution; do not recreate it.
            }
            if (followUp.Status != "Draft")
                throw new InvalidOperationException("The linked corrective delivery stopped without accepted successor evidence. Its manager must reconcile the retained work; no replacement was invented.");
        }
        if (plan.Findings.Where(f => correction.FindingIds.Contains(f.Id)).Any(f => f.Status != "Open")) return;
        var rejected = plan.Executions.Single(e => e.Id == correction.ExecutionId);
        if (await RecoverNoRunnableOutputAsync(plan, correction, rejected, context, ct)) return;
        var acceptance = plan.Scopes.Single(s => s.Scope == rejected.Scope && s.ItemId == rejected.ItemId);
        if (rejected.Scope != WorkExecutionScopes.Release || rejected.Candidate?.Repositories.Count != 1)
            throw new InvalidOperationException("Select the implementation owner and follow-up topology explicitly for this multi-source correction.");
        var board = await work.ReadBoardAsync(correction.BoardId, ct);
        var sourceIds = plan.Scopes.SelectMany(s => s.ChildIds).ToHashSet();
        var sources = board.Items.Where(i => sourceIds.Contains(i.Id) && i.Delivery is { DeliveryKind: "Code" } d &&
            d.DeliveryPlanId == plan.Id && rejected.Candidate.Repositories.Any(r => r.RepositoryId == d.RepositoryId) &&
            i.Status is "Done" or "Completed" && (correction.SourceItemId is null || i.Id == correction.SourceItemId)).ToArray();
        if (sources.Length == 0)
            throw new InvalidOperationException("The rejected candidate has no available implementation owner. Restore or plan the source task before corrective planning.");
        var (source, selectionReason) = sources.Length == 1 ? (sources[0], "Only one completed implementation input matches the rejected repository.") :
            await SelectOrAskOwnerAsync(plan, correction, sources, createPlanningClient, context, ct);
        if (correction.SourceItemId is null)
        {
            // Persist the planning decision before creating work. A lost response or cold restart
            // reuses this source rather than making another model decision under the same task key.
            var current = (await work.ReadDeliveryPlansAsync(new(plan.WorkstreamId, plan.Id), ct)).Single();
            var freshBoard = await work.ReadBoardAsync(correction.BoardId, ct);
            if (current.Revision != plan.Revision || sources.Any(input =>
                freshBoard.Items.SingleOrDefault(i => i.Id == input.Id) is not { } fresh ||
                fresh.Revision != input.Revision || fresh.PlanningRevision != input.PlanningRevision ||
                fresh.Status is not ("Done" or "Completed")))
                throw new InvalidOperationException("The corrective source or rejected plan changed during planning. Re-read current scope before selecting it.");
            plan = await work.CoordinateDeliveryCorrectionAsync(new(plan.Id, correction.Id, correction.Revision,
                $"delivery-correction:{correction.Id:N}:source:{correction.Revision}", SourceItemId: source.Id,
                WaitingReason: "Implementation source selected: " + selectionReason + " Preparing scoped corrective work."), ct);
            correction = plan.Corrections.Single(c => c.Id == correction.Id);
        }
        var branch = plan.Branches.SingleOrDefault(b => b.Scope == WorkExecutionScopes.Story && b.ItemId == source.ParentItemId &&
            b.RepositoryId == source.Delivery!.RepositoryId && b.TargetBranch == rejected.Candidate.Repositories[0].SourceBranch)
            ?? throw new InvalidOperationException("This correction needs an explicit integration topology; no direct Story-to-release binding was found.");
        if (source.Planning is null || source.StageAssignments.Count == 0)
            throw new InvalidOperationException("Restore the original accepted planning and staffed workflow before correction.");
        var findings = plan.Findings.Where(f => correction.FindingIds.Contains(f.Id)).OrderBy(f => f.Id).ToArray();
        var key = "delivery-correction:" + correction.Id.ToString("N");
        // Reuse exact original assignments; platform finalization revalidates current employees, grants and independence.
        var author = source.StageAssignments.Single(s => s.StageKey is "development" or "specialist-execution");
        var requirements = source.Planning.Requirements.Concat(findings.Select(f => $"Correct retained finding {f.Id:D}: {f.Summary}")).ToArray();
        var criteria = source.Planning.AcceptanceCriteria.Concat(findings.Select(f => $"Finding {f.Id:D} is corrected and independently verified: {f.Summary}")).ToArray();
        var planning = source.Planning with { Requirements = requirements, AcceptanceCriteria = criteria, DependencyItemIds = [source.Id] };
        var prepareSteps = plan.Scopes.Single(s => s.Scope == WorkExecutionScopes.Story && s.ItemId == source.ParentItemId).TicketWorkflow!.Steps;
        if (prepareSteps.Any(s => s.Handler != "completion"))
            throw new InvalidOperationException("Correction source preparation requires an explicitly mechanical original workflow.");
        var created = await work.CreateItemAsync(new(correction.BoardId, ("Correct: " + source.Title)[..Math.Min(500, source.Title.Length + 9)],
            CorrectionDescription(findings.Select(f => (f.Id, f.Summary))),
            source.Kind, "High", null, source.ParentItemId, null, key + ":task")
        { TypeKey = source.TypeKey, Planning = planning, AccountableOrganizationUserId = author.OrganizationUserId }, ct);
        var task = await work.ReadItemAsync(new(correction.BoardId, created.Id), ct);
        var prepDefinition = new WorkItemWorkflowDefinition(prepareSteps) { Scope = WorkExecutionScopes.Story, ScopeItemId = source.ParentItemId,
            EvidenceItemIds = [task.Id], PromoteOnCompletion = true };
        var prep = await work.CreateItemAsync(new(correction.BoardId, "Integrate corrective source", "Integrate the independently verified corrective task into the retained release source.",
            "Task", "High", null, null, null, key + ":prepare") { TypeKey = WorkItemTypeKeys.WorkflowTicketV1,
            Planning = new(["Prepare corrective source after independently verified engineering completes."],
                ["The corrective task is complete and its exact source is integrated."]) { DependencyItemIds = [task.Id], Workflow = prepDefinition },
            AccountableOrganizationUserId = correction.OwnerOrganizationUserId }, ct);
        // New graph; retain every original acceptance step and criterion. Move technical evidence before QA.
        var steps = acceptance.TicketWorkflow!.Steps.OrderBy(s => s.Handler is "build-readiness" or "compute-test" ? 0 :
            s.Key == "technical-review" ? 1 : s.Handler == "quality" ? 2 : 3).ToArray();
        var evidence = acceptance.ChildIds.Append(prep.Id).Distinct().ToArray();
        var definition = acceptance.TicketWorkflow.Definition() with { Steps = steps, EvidenceItemIds = evidence };
        var final = await work.CreateItemAsync(new(correction.BoardId, "Accept corrected increment", "Revalidate the complete corrected candidate against the unchanged increment requirements.",
            "Task", "High", null, null, null, key + ":accept") { TypeKey = WorkItemTypeKeys.WorkflowTicketV1,
            Planning = acceptance.Planning with { DependencyItemIds = acceptance.PredecessorItemIds.Append(prep.Id).Distinct().ToArray(), Workflow = definition },
            AccountableOrganizationUserId = correction.OwnerOrganizationUserId }, ct);
        WorkDeliveryTicketWorkflow Workflow(WorkItem item, WorkItemWorkflowDefinition d) => new(item.Id, d.Steps) {
            Scope=d.Scope,ScopeItemId=d.ScopeItemId,EvidenceItemIds=d.EvidenceItemIds,PromoteOnCompletion=d.PromoteOnCompletion,
            CompleteContainerItemIds=d.CompleteContainerItemIds,FinalReleaseDependencyItemIds=d.FinalReleaseDependencyItemIds };
        var follow = correction.FollowUpPlanId is { } followId ? plans.Single(p => p.Id == followId) :
            await work.ConfigureDeliveryPlanAsync(new(plan.WorkstreamId, "Corrective increment " + correction.Id.ToString("N")[..8],
                correction.OwnerOrganizationUserId, plan.EpicItemIds,
                [branch, .. plan.Branches.Where(b => b.Scope == rejected.Scope && b.ItemId == rejected.ItemId)], [], key + ":plan") {
                TicketWorkflows = [Workflow(prep,prepDefinition),Workflow(final,definition)],
                TaskAssignments = [new(task.Id,source.StageAssignments)] }, ct);
        if (task.Delivery is null)
            task = await work.FinalizeItemDeliveryAsync(new(correction.BoardId, task.Id, new(source.Delivery!.RepositoryId, requirements, criteria, source.Planning.Constraints) {
                DeliveryKind = "Code", DeliveryPlanId = follow.Id, BaseBranch = branch.SourceBranch, DependencyItemIds = [source.Id] },
                author.OrganizationUserId!.Value, source.StageAssignments, task.Revision, key + ":finalize"), ct);
        if (correction.FollowUpPlanId is null)
            await work.CoordinateDeliveryCorrectionAsync(new(plan.Id,correction.Id,correction.Revision,key + ":bind",
                source.Id,follow.Id,[task.Id]),ct);
        follow = (await work.ReadDeliveryPlansAsync(new(plan.WorkstreamId,follow.Id),ct)).Single();
        if (follow.Status == "Draft")
            await work.ControlDeliveryPlanAsync(new(follow.Id,follow.Revision,"activate",key + ":activate"),ct);
        // A separate repair sprint preserves the original completed sprint. An unrelated active sprint is an explicit wait.
        await EnsureSprintAsync(correction, task.Id, context, ct);
    }

    /// <summary>
    /// The corrective ticket's description names every inherited finding and says the whole candidate is re-verified, so
    /// the author does not mistake the scope for the one finding that triggered it (Super Awesome Games ticket 38, 2026-11-13).
    /// </summary>
    internal static string CorrectionDescription(IEnumerable<(Guid Id, string Summary)> findings)
    {
        var text = new System.Text.StringBuilder("Correct the retained rejected candidate within its existing requirements.\n\nFindings to correct:\n");
        foreach (var (id, summary) in findings)
            text.Append("- ").Append(id.ToString("D")).Append(": ").Append(summary.Length > 600 ? summary[..597] + "..." : summary).Append('\n');
        text.Append("\nThe corrected candidate is re-verified in full, not only for these findings: technical review and independent QA check " +
            "every original acceptance criterion again, so a defect found anywhere in the task still blocks it. Before publishing, " +
            "re-check the whole task against its criteria, including observed behavior and any observation record. Preserve previous " +
            "accepted work and add regression coverage for each finding.");
        var value = text.ToString();
        return value.Length <= 4000 ? value : value[..3997] + "...";
    }

    private static async Task EnsureSprintAsync(WorkDeliveryCorrection correction, Guid taskId, AgentRuntimeContext context, CancellationToken ct)
    {
        var work = context.Platform.Work;
        var key = "delivery-correction:" + correction.Id.ToString("N");
        var task = await work.ReadItemAsync(new(correction.BoardId,taskId),ct);
        if (task.Status is "Done" or "Completed") return;
        var sprints = await work.ListSprintsAsync(correction.BoardId,ct);
        var sprint = await work.CreateSprintAsync(new(correction.BoardId,"Corrective increment " + correction.Id.ToString("N")[..8],
            "Resolve retained findings and independently validate the corrected increment.",null,null,key + ":sprint"),ct);
        sprint = sprints.SingleOrDefault(s => s.Id == sprint.Id) ?? sprint;
        if (sprint.Status == "Active") return;
        if (sprints.Any(s => s.Status == "Active" && s.Id != sprint.Id))
            throw new InvalidOperationException("Finish or explicitly reconcile the currently active sprint before starting the separate corrective sprint.");
        task = await work.ReadItemAsync(new(correction.BoardId,task.Id),ct);
        if (task.SprintId != sprint.Id)
            await work.SetItemSprintAsync(new(correction.BoardId,task.Id,sprint.Id,task.Revision,key + ":schedule"),ct);
        sprint = (await work.ListSprintsAsync(correction.BoardId,ct)).Single(s => s.Id == sprint.Id);
        var start = new StartWorkSprintExecutionRequest(correction.BoardId,sprint.Id,sprint.Revision,key + ":start");
        var preflight = await work.PreflightSprintAsync(start,ct);
        if (!preflight.IsValid) throw new InvalidOperationException("Corrective sprint preflight: " + string.Join("; ",preflight.Errors.Select(e=>e.Message)));
        await work.StartSprintExecutionAsync(start,ct);
    }

    private static async Task RetainWaitAsync(WorkDeliveryPlanResponse plan, WorkDeliveryCorrection correction, string reason,
        AgentRuntimeContext context, CancellationToken ct)
    {
        reason = reason[..Math.Min(2000,reason.Length)];
        // No unchanged presentation writes/wake loops; a new assignment or grant event may retry current state.
        var current = (await context.Platform.Work.ReadDeliveryPlansAsync(new(plan.WorkstreamId,plan.Id),ct)).Single();
        var saved = current.Corrections.Single(c=>c.Id==correction.Id);
        if (saved.WaitingReason == reason || saved.Status == "Completed") return;
        await context.Platform.Work.CoordinateDeliveryCorrectionAsync(new(plan.Id,saved.Id,saved.Revision,
            $"correction-wait:{saved.Id:N}:{saved.Revision}",WaitingReason:reason),ct);
    }
}
