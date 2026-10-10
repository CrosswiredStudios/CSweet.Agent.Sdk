using System.Security.Cryptography;
using System.Text;
using CSweet.WorkManagement.Contracts;

namespace CSweet.Agent.SDK;

/// <summary>Manager-owned setup of a revision-pinned hierarchy, independent staffing and shared release branches.</summary>
public static class HierarchicalProjectDelivery
{
    public static async Task<WorkDeliveryPlanResponse> PrepareAsync(Guid projectId, Guid boardId,
        Guid managerId, AgentTeamContext roster, Guid repositoryId, string defaultBranch,
        string profileDigest, bool software, AgentRuntimeContext context, CancellationToken ct, string? technicalRoleOverride = null)
    {
        if (profileDigest.Length != 64 || profileDigest.Any(x => !Uri.IsHexDigit(x)))
            profileDigest = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(profileDigest)));
        var allPlans = await context.Platform.Work.ReadDeliveryPlansAsync(new(projectId), ct);
        var existing = allPlans
            .Where(x => x.Status is not ("Cancelled" or "Completed")).ToArray();
        if (existing.Length > 1) throw new InvalidOperationException("Select the existing release explicitly; project setup cannot choose between multiple delivery plans.");
        if (existing.SingleOrDefault() is { Status: "Active" or "Paused" } active) return active;
        var board = await context.Platform.Work.ReadBoardAsync(boardId, ct);
        if (board.Items.Where(x => x.ExecutionMode == WorkItemExecutionModes.Executable && x.Status != "Cancelled")
            .All(x => x.Status is "Done" or "Completed") && allPlans.Where(x => x.Status == "Completed")
            .OrderByDescending(x => x.UpdatedAt).FirstOrDefault() is { } completed) return completed;
        var acceptedEpics = allPlans.Where(x => x.Status == "Completed").SelectMany(x => x.EpicItemIds).ToHashSet();
        var scopedEpics = existing.SingleOrDefault()?.EpicItemIds.ToHashSet() ?? board.Items
            .Where(x => x.Kind == "Epic" && x.ExecutionMode == WorkItemExecutionModes.Container &&
                x.Status is not ("Done" or "Completed" or "Cancelled") && !acceptedEpics.Contains(x.Id)).Select(x => x.Id).ToHashSet();
        var scopedStories = board.Items.Where(x => x.Kind == "Story" && x.ParentItemId.HasValue && scopedEpics.Contains(x.ParentItemId.Value)).Select(x => x.Id).ToHashSet();
        var tasks = board.Items.Where(x => x.ExecutionMode == WorkItemExecutionModes.Executable &&
            x.ParentItemId.HasValue && scopedStories.Contains(x.ParentItemId.Value)).ToArray();
        if (tasks.Length == 0) throw new InvalidOperationException("Plan at least one executable task within a story and epic before activating delivery.");
        foreach (var task in tasks)
        {
            var story = board.Items.SingleOrDefault(x => x.Id == task.ParentItemId && x.Kind == "Story" && x.ExecutionMode == WorkItemExecutionModes.Container);
            if (task.Planning is null || task.Planning.AcceptanceCriteria.Count == 0 || story is null ||
                !board.Items.Any(x => x.Id == story.ParentItemId && x.Kind == "Epic" && x.ExecutionMode == WorkItemExecutionModes.Container))
                throw new InvalidOperationException("Every project task needs accepted criteria and a board-local story and epic, including small fixes.");
        }
        // Parent criteria are recorded before plan scope pins child planning revisions.
        foreach (var parent in board.Items.Where(x => scopedStories.Contains(x.Id) || scopedEpics.Contains(x.Id)).OrderBy(x => x.Kind == "Story" ? 0 : 1))
        {
            if (parent.Planning?.AcceptanceCriteria.Count > 0) continue;
            board = await context.Platform.Work.ReadBoardAsync(boardId, ct);
            var children = board.Items.Where(x => x.ParentItemId == parent.Id).ToArray();
            if (children.Length == 0 || children.Any(x => x.Planning?.AcceptanceCriteria.Count is null or 0))
                throw new InvalidOperationException("Every scoped story and epic needs nonempty, agreed child work and acceptance criteria.");
            var planning = new WorkItemPlanningSpecification(children.SelectMany(x => x.Planning!.Requirements).Distinct().ToArray(),
                children.SelectMany(x => x.Planning!.AcceptanceCriteria).Distinct().ToArray());
            await context.Platform.Work.RevisePlanningAsync(new(boardId, parent.Id, parent.Title, parent.Description,
                parent.ParentItemId, planning, parent.Revision, parent.PlanningRevision, $"delivery-parent:{parent.Id:N}:{parent.PlanningRevision}"), ct);
        }
        board = await context.Platform.Work.ReadBoardAsync(boardId, ct);
        tasks = board.Items.Where(x => x.ExecutionMode == WorkItemExecutionModes.Executable &&
            x.ParentItemId.HasValue && scopedStories.Contains(x.ParentItemId.Value)).ToArray();
        var selected = new Dictionary<Guid, (WorkStageAssignment Author, WorkStageAssignment Qa, WorkStageAssignment? Technical, bool Code, bool QaEvidence)>();
        var qaRole = software ? "software-qa" : "game-quality-assurance";
        var technicalRole = technicalRoleOverride ?? (software ? "software-architect" : "game-technical-director");
        foreach (var task in tasks)
        {
            var authorKey = software ? "development" : "specialist-execution";
            var recommendation = task.Planning!.DelegationRecommendations.FirstOrDefault(x => x.StageKey == authorKey);
            var prior = task.StageAssignments.FirstOrDefault(x => x.StageKey == authorKey);
            var authorRole = recommendation?.RequiredRoleKey ?? prior?.Requirements?.RequiredRoleKey ??
                (software ? "software-developer" : "game-engineer");
            var author = Select(authorKey, authorRole, [], prior?.AgentInstallationId);
            var code = task.Delivery?.DeliveryKind != "Artifact" && task.Planning.DeliveryKind != "Artifact" && authorRole is "software-developer" or "game-engineer";
            var qaEvidence = DeliveryReviewIndependence.IsQaEvidenceArtifact(task.Delivery?.DeliveryKind ?? task.Planning.DeliveryKind, authorRole);
            // QA owns test plans/reports; the board manager checks their evidence. This is
            // distinct from independent QA of the implementation under test.
            var qa = qaEvidence
                ? Select("quality", "manager", [author.OrganizationUserId!.Value], requiredEmployee: managerId, subject: task.Title)
                : Select("quality", qaRole, [author.OrganizationUserId!.Value], subject: task.Title);
            var technical = code ? Select("technical-review", technicalRole, [author.OrganizationUserId!.Value]) : null;
            selected.Add(task.Id, (author, qa, technical, code, qaEvidence));
        }
        var productAuthors = selected.Values.Where(x => !x.QaEvidence).Select(x => x.Author.OrganizationUserId!.Value).Distinct().ToArray();
        var codeAuthors = selected.Values.Where(x => x.Code).Select(x => x.Author.OrganizationUserId!.Value).Distinct().ToArray();
        var scopes = new List<WorkDeliveryScopeAssignment>();
        var branches = new List<WorkDeliveryBranchBinding>();
        var releaseScopeKey = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("|", scopedEpics.Order()))))[..16];
        var releaseBranch = $"codex/release/{projectId:N}/{releaseScopeKey}";
        foreach (var story in board.Items.Where(x => scopedStories.Contains(x.Id)))
        {
            var childIds = tasks.Where(x => x.ParentItemId == story.Id).Select(x => x.Id).ToArray();
            scopes.Add(new(WorkExecutionScopes.Story, story.Id, boardId,
                [Select("quality", qaRole, childIds.Where(x => !selected[x].QaEvidence).Select(x => selected[x].Author.OrganizationUserId!.Value).ToArray())]));
            if (childIds.Any(id => selected[id].Code)) branches.Add(new(repositoryId, WorkExecutionScopes.Story, story.Id,
                $"codex/story/{story.Id:N}", releaseBranch));
        }
        var epics = board.Items.Where(x => scopedEpics.Contains(x.Id)).ToArray();
        foreach (var epic in epics) scopes.Add(new(WorkExecutionScopes.Epic, epic.Id, boardId,
            [Select("technical-review", technicalRole, codeAuthors)]));
        var releaseStages = new List<WorkStageAssignment> { Select("quality", qaRole, productAuthors), Select("technical-review", technicalRole, codeAuthors) };
        if (!software && branches.Count > 0) releaseStages.Add(Select("build-readiness", "game-build-release-engineer", []));
        scopes.Add(new(WorkExecutionScopes.Release, null, boardId, releaseStages));
        if (branches.Count > 0)
        {
            if (repositoryId == Guid.Empty || string.IsNullOrWhiteSpace(defaultBranch)) throw new InvalidOperationException("Code delivery needs an authorized repository and default branch.");
            WorkDeliveryBuildSpecification? build = null;
            if (!software)
            {
                var adapters = await context.Platform.ReadEligibleToolchainsAsync(new(RequiredOperations: ["build"]), ct);
                var recipes = adapters.Where(a => a.Eligibility.ExpiresAt > DateTimeOffset.UtcNow && a.Eligibility.CompatibleCapacityOnline)
                    .SelectMany(a => a.Definition.Recipes.Where(r => r.Operations.Contains("build")).SelectMany(r => r.TargetKeys.Select(t =>
                        new WorkDeliveryBuildSpecification(a.Definition.Id, a.Eligibility.ProviderInstallationId, r.Key, t,
                            System.Text.Json.JsonSerializer.SerializeToElement(new { }))))).ToArray();
                if (recipes.Length != 1) throw new InvalidOperationException("The architect must select an explicit certified build recipe, provider, configuration and target before activating game release delivery.");
                build = recipes[0];
            }
            branches.Add(new(repositoryId, WorkExecutionScopes.Release, null, releaseBranch, defaultBranch) { Build = build });
        }
        var draft = existing.SingleOrDefault();
        var plan = draft ?? await context.Platform.Work.ConfigureDeliveryPlanAsync(new(projectId, "Initial release", managerId,
            epics.Select(x => x.Id).ToArray(), branches, scopes, $"delivery-plan:{projectId:N}:{releaseScopeKey}:{profileDigest}"), ct);
        foreach (var task in tasks)
        {
            if (task.Delivery?.DeliveryPlanId == plan.Id) continue;
            var assignment = selected[task.Id];
            var stages = new List<WorkStageAssignment> { assignment.Author, assignment.Qa };
            if (assignment.Technical is not null)
            {
                stages.Add(assignment.Technical);
                stages.Add(new("task-integration", "PlatformAction", PlatformAction: "source-control.task.integrate.v1"));
            }
            var planning = task.Planning!;
            var delivery = new WorkItemDeliverySpecification(assignment.Code ? repositoryId : Guid.Empty,
                planning.Requirements, planning.AcceptanceCriteria, planning.Constraints)
            {
                DeliveryPlanId = plan.Id, DeliveryKind = assignment.Code ? "Code" : "Artifact",
                BaseBranch = assignment.Code ? plan.Branches.Single(x => x.Scope == WorkExecutionScopes.Story && x.ItemId == task.ParentItemId && x.RepositoryId == repositoryId).SourceBranch : "",
                DependencyItemIds = planning.DependencyItemIds
            };
            var current = await context.Platform.Work.ReadItemAsync(new(boardId, task.Id), ct);
            await context.Platform.Work.FinalizeItemDeliveryAsync(new(boardId, task.Id, delivery,
                assignment.Author.OrganizationUserId!.Value, stages, current.Revision,
                $"delivery-task:{plan.Id:N}:{task.Id:N}:{task.PlanningRevision}:{roster.Revision}"), ct);
        }
        plan = (await context.Platform.Work.ReadDeliveryPlansAsync(new(projectId, plan.Id), ct)).Single();
        return await context.Platform.Work.ControlDeliveryPlanAsync(new(plan.Id, plan.Revision, "activate", $"delivery-activate:{plan.Id:N}:{plan.Revision}"), ct);

        WorkStageAssignment Select(string stage, string role, IReadOnlyList<Guid> excluded, Guid? preferred = null, Guid? requiredEmployee = null, string? subject = null)
        {
            var requirements = new WorkAssignmentRequirements(role, [], [], [WorkManagementCapabilityNames.ExecutionRunV2]);
            var eligible = roster.Members.Where(x => Guid.TryParse(x.EmployeeId, out var employee) && !excluded.Contains(employee) && (!requiredEmployee.HasValue || employee == requiredEmployee)).ToArray();
            var candidate = preferred.HasValue ? RoleTaxonomy.SelectAssignment(eligible.Where(x => x.AgentInstallationId == preferred), requirements)?.Teammate : null;
            candidate ??= RoleTaxonomy.SelectAssignment(eligible, requirements)?.Teammate
                ?? throw new DeliveryStaffingException($"Staffing needs attention for {subject ?? stage}: assign an eligible {role} with V2 execution to {stage}, independent of the deliverable author. Required review cannot be skipped.");
            var fingerprint = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"{projectId:N}|{stage}|{role}|{candidate.AgentInstallationId}|{roster.Revision}|{profileDigest}")));
            return new(stage, "AgentInstallation", Guid.Parse(candidate.EmployeeId), candidate.AgentInstallationId)
            {
                Requirements = requirements,
                SelectionEvidence = new(candidate.AgentInstallationId!.Value, roster.Revision, profileDigest, [], fingerprint, DateTimeOffset.UtcNow)
            };
        }
    }
}
