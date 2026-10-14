using System.Security.Cryptography;
using System.Text;
using CSweet.WorkManagement.Contracts;

namespace CSweet.Agent.SDK;

/// <summary>Manager-owned setup of a revision-pinned hierarchy, independent staffing and shared release branches.</summary>
public static class HierarchicalProjectDelivery
{
    internal static WorkAssignmentRequirements BuildReadinessRequirements(string role) => new(role, [], [],
        role == "game-build-release-engineer" ? [WorkManagementCapabilityNames.ExecutionRun] :
            [WorkManagementCapabilityNames.ExecutionRun, DeliveryBuildReadiness.ExecutionCapability]);

    internal static string SelectBuildReadinessRole(AgentTeamContext roster) =>
        new[] { "game-build-release-engineer", "game-technical-director", "software-developer" }
            .FirstOrDefault(role => RoleTaxonomy.SelectAssignment(roster.Members, BuildReadinessRequirements(role)) is not null)
        ?? throw new DeliveryStaffingException("Build readiness needs an eligible Build Release Engineer, Technical Director, or Developer with canonical execution and build-readiness support. Certified build evidence is still required.");

    public static async Task<WorkDeliveryPlanResponse> PrepareAsync(Guid projectId, Guid boardId,
        Guid managerId, AgentTeamContext roster, Guid repositoryId, string defaultBranch,
        string profileDigest, bool software, AgentRuntimeContext context, CancellationToken ct, string? technicalRoleOverride = null)
    {
        try
        {
            return await PrepareCoreAsync(projectId, boardId, managerId, roster, repositoryId, defaultBranch,
                profileDigest, software, context, ct, technicalRoleOverride);
        }
        catch (PlatformCapabilityException error) when (DeliveryConflictReason(error) is not null)
        {
            throw new DeliveryStaffingException("Delivery planning needs attention: " + DeliveryConflictReason(error));
        }
    }

    internal static string? DeliveryConflictReason(PlatformCapabilityException error)
    {
        if (error.Capability is not (WorkDeliveryCapabilities.Configure or WorkDeliveryCapabilities.Control or WorkManagementCapabilityNames.ItemFinalizeDelivery)) return null;
        var reason = error.Message;
        var conflict = error.Code == PlatformCapabilityErrorCode.Conflict;
        if (reason.TrimStart().StartsWith('{'))
        {
            try
            {
                using var envelope = System.Text.Json.JsonDocument.Parse(reason);
                conflict |= envelope.RootElement.TryGetProperty("code", out var code) && code.GetString() == "Conflict";
                if (envelope.RootElement.TryGetProperty("message", out var message) && message.ValueKind == System.Text.Json.JsonValueKind.String)
                    reason = message.GetString()!;
            }
            catch (System.Text.Json.JsonException) { }
        }
        return conflict ? reason[..Math.Min(reason.Length, 2000)] : null;
    }

    private static bool Same(object? left, object? right)
    {
        object? Stable(object? value) => value is IEnumerable<WorkStageAssignment> stages
            ? stages.OrderBy(stage => stage.StageKey, StringComparer.Ordinal).Select(stage => stage with { SelectionEvidence = stage.SelectionEvidence is { } evidence
                ? evidence with { SelectedAt = DateTimeOffset.UnixEpoch } : null }).ToArray() : value;
        return System.Text.Json.JsonSerializer.Serialize(Stable(left)) == System.Text.Json.JsonSerializer.Serialize(Stable(right));
    }

    private static async Task<WorkDeliveryPlanResponse> PrepareCoreAsync(Guid projectId, Guid boardId,
        Guid managerId, AgentTeamContext roster, Guid repositoryId, string defaultBranch,
        string profileDigest, bool software, AgentRuntimeContext context, CancellationToken ct, string? technicalRoleOverride)
    {
        if (profileDigest.Length != 64 || profileDigest.Any(x => !Uri.IsHexDigit(x)))
            profileDigest = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(profileDigest)));
        var allPlans = await context.Platform.Work.ReadDeliveryPlansAsync(new(projectId), ct);
        var existing = allPlans
            .Where(x => x.Status is not ("Cancelled" or "Completed")).ToArray();
        if (existing.Length > 1) throw new InvalidOperationException("Select the existing release explicitly; project setup cannot choose between multiple delivery plans.");
        if (existing.SingleOrDefault() is { Status: "Active" or "Paused" } active) return active;
        var draft = existing.SingleOrDefault();
        if (draft is not null && (draft.ManagerOrganizationUserId != managerId || draft.Scopes.Any(x => x.BoardId != boardId) || draft.Branches.Any(x => x.RepositoryId != repositoryId)))
            throw new DeliveryStaffingException("The existing draft belongs to another manager or board; reconcile its scope explicitly.");
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
            var authorRequirements = recommendation is null ? prior?.Requirements : new WorkAssignmentRequirements(authorRole,
                recommendation.RequiredSpecializationKeys, recommendation.PreferredSpecializationKeys, recommendation.RequiredCapabilityKeys);
            var author = Select(authorKey, authorRole, [], prior?.AgentInstallationId, requestedRequirements: authorRequirements);
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
        var computeReviewer = RoleTaxonomy.SelectAssignment(roster.Members,
            new WorkAssignmentRequirements("game-technical-director", [], [],
                [WorkManagementCapabilityNames.ExecutionRun, DeliveryComputeTest.ExecutionCapability]))?.Teammate;
        var retainedRelease = draft?.Branches.SingleOrDefault(x => x.Scope == WorkExecutionScopes.Release);
        var useComputeTest = !software && branches.Count > 0 && (retainedRelease is not null
            ? retainedRelease.ReadinessMode == "compute-test" : computeReviewer is not null);
        var releaseStages = new List<WorkStageAssignment> { Select("quality", qaRole, productAuthors), Select("technical-review", technicalRole, codeAuthors) };
        if (!software && branches.Count > 0)
        {
            // Certified builds are mechanical evidence, not independent product review.
            // Prefer the release specialist; updated technical/development agents can own this gate too.
            var role = useComputeTest ? "game-technical-director" : SelectBuildReadinessRole(roster);
            releaseStages.Add(Select("build-readiness", role, [], additionalCapabilities:
                useComputeTest ? [DeliveryComputeTest.ExecutionCapability] : role == "game-build-release-engineer" ? [] : [DeliveryBuildReadiness.ExecutionCapability]));
        }
        scopes.Add(new(WorkExecutionScopes.Release, null, boardId, releaseStages));
        if (branches.Count > 0)
        {
            if (repositoryId == Guid.Empty || string.IsNullOrWhiteSpace(defaultBranch)) throw new InvalidOperationException("Code delivery needs an authorized repository and default branch.");
            WorkDeliveryBuildSpecification? build = retainedRelease?.Build;
            if (!software && !useComputeTest && build is null)
            {
                var adapters = await context.Platform.ReadEligibleToolchainsAsync(new(RequiredOperations: ["build"]), ct);
                var recipes = adapters.Where(a => a.Eligibility.ExpiresAt > DateTimeOffset.UtcNow && a.Eligibility.CompatibleCapacityOnline)
                    .SelectMany(a => a.Definition.Recipes.Where(r => r.Operations.Contains("build")).SelectMany(r => r.TargetKeys.Select(t =>
                        new WorkDeliveryBuildSpecification(a.Definition.Id, a.Eligibility.ProviderInstallationId, r.Key, t,
                            System.Text.Json.JsonSerializer.SerializeToElement(new { }))))).ToArray();
                if (recipes.Length != 1) throw new DeliveryStaffingException("Delivery readiness needs either a Technical Director with compute-test support or an explicitly selected certified build recipe, provider, configuration and target. Sprint setup will resume after the available option is selected.");
                build = recipes[0];
            }
            branches.Add(new(repositoryId, WorkExecutionScopes.Release, null, releaseBranch, defaultBranch) { Build = build, ReadinessMode = useComputeTest ? "compute-test" : "certified-build" });
        }
        // Retain explicitly selected branch/build topology when refreshing a draft.
        if (draft is not null)
            branches = branches.Select(branch => draft.Branches.SingleOrDefault(x => x.Scope == branch.Scope &&
                x.ItemId == branch.ItemId && x.RepositoryId == branch.RepositoryId) ?? branch).ToList();
        var scopeChanged = draft is not null && draft.Scopes.Any(scope =>
            scope.ItemId is { } itemId && board.Items.SingleOrDefault(x => x.Id == itemId)?.PlanningRevision != scope.PlanningRevision ||
            scope.ChildPlanningRevisions.Any(child => board.Items.SingleOrDefault(x => x.Id == child.Key)?.PlanningRevision != child.Value) ||
            !scope.ChildIds.Order().SequenceEqual((scope.Scope == WorkExecutionScopes.Release ? epics.Select(x => x.Id) :
                board.Items.Where(x => x.ParentItemId == scope.ItemId).Select(x => x.Id)).Order()));
        var assignmentsChanged = draft is not null && scopes.Any(scope => !draft.Scopes.Any(x =>
            x.Scope == scope.Scope && x.ItemId == scope.ItemId && Same(x.Stages, scope.Stages)));
        var plan = draft is not null && !scopeChanged && !assignmentsChanged ? draft : await context.Platform.Work.ConfigureDeliveryPlanAsync(new(projectId, draft?.Name ?? "Initial release", managerId,
            epics.Select(x => x.Id).ToArray(), branches, scopes, $"delivery-plan:{projectId:N}:{releaseScopeKey}:{profileDigest}:{draft?.Revision ?? 0}:{roster.Revision}")
            {
                PlanId = draft?.Id, ExpectedRevision = draft?.Revision ?? 0,
                ReleasePlanning = draft?.Scopes.SingleOrDefault(x => x.Scope == WorkExecutionScopes.Release)?.Planning,
                TaskAssignments = tasks.Select(task => new WorkDeliveryTaskAssignment(task.Id,
                    new[] { selected[task.Id].Author, selected[task.Id].Qa })).ToArray()
            }, ct);
        foreach (var task in tasks)
        {
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
            if (Same(current.Delivery, delivery) && Same(current.StageAssignments, stages)) continue;
            await context.Platform.Work.FinalizeItemDeliveryAsync(new(boardId, task.Id, delivery,
                assignment.Author.OrganizationUserId!.Value, stages, current.Revision,
                $"delivery-task:{plan.Id:N}:{task.Id:N}:{task.PlanningRevision}:{roster.Revision}:{current.Revision}"), ct);
        }
        plan = (await context.Platform.Work.ReadDeliveryPlansAsync(new(projectId, plan.Id), ct)).Single();
        return await context.Platform.Work.ControlDeliveryPlanAsync(new(plan.Id, plan.Revision, "activate", $"delivery-activate:{plan.Id:N}:{plan.Revision}"), ct);

        WorkStageAssignment Select(string stage, string role, IReadOnlyList<Guid> excluded, Guid? preferred = null, Guid? requiredEmployee = null, string? subject = null, IReadOnlyList<string>? additionalCapabilities = null, WorkAssignmentRequirements? requestedRequirements = null)
        {
            var requirements = new WorkAssignmentRequirements(role, requestedRequirements?.RequiredSpecializationKeys ?? [],
                requestedRequirements?.PreferredSpecializationKeys ?? [],
                [.. (requestedRequirements?.RequiredCapabilityKeys ?? []).Concat([WorkManagementCapabilityNames.ExecutionRun])
                    .Concat(additionalCapabilities ?? []).Distinct(StringComparer.Ordinal)]);
            var eligible = roster.Members.Where(x => Guid.TryParse(x.EmployeeId, out var employee) && !excluded.Contains(employee) && (!requiredEmployee.HasValue || employee == requiredEmployee)).ToArray();
            var candidate = preferred.HasValue ? RoleTaxonomy.SelectAssignment(eligible.Where(x => x.AgentInstallationId == preferred), requirements)?.Teammate : null;
            candidate ??= RoleTaxonomy.SelectAssignment(eligible, requirements)?.Teammate
                ?? throw new DeliveryStaffingException($"Staffing needs attention for {subject ?? stage}: assign an eligible {role} with canonical execution to {stage}, independent of the deliverable author. Required review cannot be skipped.");
            var matched = requirements.RequiredSpecializationKeys.Concat(requirements.PreferredSpecializationKeys
                .Where(x => candidate.SpecializationKeys.Contains(x, StringComparer.Ordinal))).Distinct().Order(StringComparer.Ordinal).ToArray();
            var fingerprint = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"{projectId:N}|{stage}|{role}|{candidate.AgentInstallationId}|{roster.Revision}|{profileDigest}|{System.Text.Json.JsonSerializer.Serialize(requirements)}")));
            var retained = tasks.SelectMany(x => x.StageAssignments).Concat(draft?.Scopes.SelectMany(x => x.Stages) ?? [])
                .FirstOrDefault(x => x.StageKey == stage && x.AgentInstallationId == candidate.AgentInstallationId &&
                    x.OrganizationUserId == Guid.Parse(candidate.EmployeeId) && Same(x.Requirements, requirements) &&
                    x.SelectionEvidence?.DecisionFingerprint == fingerprint);
            if (retained is not null) return retained;
            return new(stage, "AgentInstallation", Guid.Parse(candidate.EmployeeId), candidate.AgentInstallationId)
            {
                Requirements = requirements,
                SelectionEvidence = new(candidate.AgentInstallationId!.Value, roster.Revision, profileDigest, matched, fingerprint, DateTimeOffset.UtcNow)
            };
        }
    }
}
