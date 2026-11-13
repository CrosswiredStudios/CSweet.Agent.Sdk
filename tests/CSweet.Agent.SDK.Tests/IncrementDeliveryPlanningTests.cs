using System.Text.Json;
using CSweet.WorkManagement.Contracts;

namespace CSweet.Agent.SDK.Tests;

public sealed class IncrementDeliveryPlanningTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SuccessorIncrementTargetsSharedInternalBranchAndRetainsFinalReleaseAsSeparateBoundary(bool selectStories)
    {
        var project = Guid.NewGuid(); var boardId = Guid.NewGuid(); var manager = Guid.NewGuid(); var repository = Guid.NewGuid();
        AgentTeammate Member(string role) => new(Guid.NewGuid().ToString(), role, "Agent", null, null, "Peer", "Online") {
            AgentInstallationId = Guid.NewGuid(), IsAvailable = true, RuntimeEligibility = "Eligible", DeclaredRoleKeys = [role],
            EffectiveCapabilities = [WorkManagementCapabilityNames.ExecutionRun, DeliveryComputeTest.ExecutionCapability] };
        var roster = new AgentTeamContext(Guid.NewGuid().ToString(), "Game", "Game", 1, manager.ToString(), "Producer",
            [Member("game-engineer"), Member("game-quality-assurance"), Member("game-technical-director"), Member("game-producer") with { EmployeeId = manager.ToString() }], [], 0, false);
        var planning = new WorkItemPlanningSpecification(["Implement next feature"], ["Next feature works"]);
        var epic = JsonSerializer.Deserialize<WorkItem>("{}")! with { Id = Guid.NewGuid(), Kind = "Epic", Title = "Next increment",
            Status = "Ready", Planning = planning, PlanningRevision = 1, ExecutionMode = WorkItemExecutionModes.Container, StageAssignments = [] };
        var story = epic with { Id = Guid.NewGuid(), Kind = "Story", ParentItemId = epic.Id };
        var task = story with { Id = Guid.NewGuid(), Kind = "Task", ParentItemId = story.Id, ExecutionMode = WorkItemExecutionModes.Executable };
        var items = new List<WorkItem> { epic, story, task };
        var sibling = story with { Id = Guid.NewGuid(), Planning = new(["Add scoring"], ["A hit scores once"]) };
        var siblingTask = task with { Id = Guid.NewGuid(), ParentItemId = sibling.Id, Planning = sibling.Planning };
        if (selectStories) items.AddRange([sibling, siblingTask]);
        var board = new WorkBoardSummary(boardId, "GAME", "Game", false, false, 1, []) { WorkstreamId = project, ManagerOrganizationUserId = manager };
        var previous = JsonSerializer.Deserialize<WorkDeliveryPlanResponse>("{}")! with { Id = Guid.NewGuid(), Status = "Completed",
            EpicItemIds = [Guid.NewGuid()], UpdatedAt = DateTimeOffset.UtcNow, Branches = [], Scopes = [], Executions = [] };
        var plans = new List<WorkDeliveryPlanResponse> { previous }; ConfigureWorkDeliveryPlanRequest? configured = null;
        var runtime = new AgentTestRuntime()
            .RegisterCapability<CreateWorkItemRequest, WorkItem>(WorkItemCapabilities.Create, (r, _) => {
                    Assert.InRange(r.IdempotencyKey.Length, 1, 160);
                    var item = JsonSerializer.Deserialize<WorkItem>("{}")! with { Id = Guid.NewGuid(),
                        Title = r.Title, Kind = r.Kind, TypeKey = r.TypeKey!, Planning = r.Planning, PlanningRevision = 1,
                        StageAssignments = [], ExecutionMode = WorkItemExecutionModes.Container };
                    return Task.FromResult(item); })
            .RegisterCapability<ReadWorkDeliveryPlansRequest, IReadOnlyList<WorkDeliveryPlanResponse>>(WorkDeliveryCapabilities.Read,
                (r, _) => Task.FromResult<IReadOnlyList<WorkDeliveryPlanResponse>>(plans.Where(p => r.PlanId is null || r.PlanId == p.Id).ToArray()))
            .RegisterCapability<WorkBoardReference, WorkBoardDetail>(WorkItemCapabilities.Read, (_, _) => Task.FromResult(new WorkBoardDetail(board, [], items)))
            .RegisterCapability<ConfigureWorkDeliveryPlanRequest, WorkDeliveryPlanResponse>(WorkDeliveryCapabilities.Configure, (r, _) => {
                configured = r;
                var plan = previous with { Id = Guid.NewGuid(), Status = "Draft", EpicItemIds = r.EpicItemIds, Branches = r.Branches, ManagerOrganizationUserId = manager };
                plans.Add(plan); return Task.FromResult(plan); })
            .RegisterCapability<FinalizeWorkItemDeliveryRequest, WorkItem>(WorkItemCapabilities.FinalizeDelivery, (r, _) => {
                var index = items.FindIndex(x => x.Id == r.ItemId);
                items[index] = items[index] with { Delivery = r.Delivery, StageAssignments = r.StageAssignments }; return Task.FromResult(items[index]); })
            .RegisterCapability<ControlWorkDeliveryPlanRequest, WorkDeliveryPlanResponse>(WorkDeliveryCapabilities.Control,
                (r, _) => Task.FromResult(plans.Single(p => p.Id == r.PlanId) with { Status = "Active" }));
        Task<WorkDeliveryPlanResponse> PrepareStory(Guid id) => HierarchicalProjectDelivery.PrepareStoryIncrementAsync(project, boardId, manager, roster, repository,
            "main", new string('a', 64), false, [id], runtime.CreateContext(), default, completeFullyDefinedEpics: true);
        var result = selectStories ? await PrepareStory(story.Id) : await HierarchicalProjectDelivery.PrepareIncrementAsync(project, boardId, manager, roster, repository,
            "main", new string('a', 64), false, [epic.Id], runtime.CreateContext(), default);
        Assert.Equal("Active", result.Status);
        var release = Assert.Single(configured!.Branches, b => b.Scope == WorkExecutionScopes.Release);
        Assert.Equal($"csweet/integration/{project:N}", release.TargetBranch);
        Assert.DoesNotContain(configured.Branches, b => b.TargetBranch == "main");
        Assert.Equal(release.SourceBranch, Assert.Single(configured.Branches, b => b.Scope == WorkExecutionScopes.Story).TargetBranch);
        Assert.Equal(configured.EpicItemIds, new[] { epic.Id });
        Assert.Contains(configured.Assignments.Single(a => a.Scope == WorkExecutionScopes.Release).Stages, s => s.StageKey == "quality");
        Assert.Contains(configured.Assignments.Single(a => a.Scope == WorkExecutionScopes.Release).Stages, s => s.StageKey == "technical-review");
        Assert.Equal(new[] {"build-readiness", "technical-review", "quality", "manager-review"},
            configured.Assignments.Single(a => a.Scope == WorkExecutionScopes.Release).Stages.Select(s => s.StageKey));
        if (selectStories)
        {
            Assert.Equal(new[] { story.Id }, configured.TicketWorkflows.Single(w => w.Scope == WorkExecutionScopes.Release).CompleteContainerItemIds);
            Assert.DoesNotContain(configured.TaskAssignments, a => a.ItemId == siblingTask.Id);
            Assert.Equal(story.Planning!.AcceptanceCriteria, configured.ReleasePlanning!.AcceptanceCriteria);
            plans[^1] = result with { Status = "Completed", Scopes = [new(WorkExecutionScopes.Story, story.Id, boardId, 1, [], story.Planning, [])] };
            await Assert.ThrowsAsync<InvalidOperationException>(() => PrepareStory(story.Id));
            await PrepareStory(sibling.Id);
            Assert.True(new HashSet<Guid> { sibling.Id, epic.Id }.SetEquals(configured.TicketWorkflows.Single(w => w.Scope == WorkExecutionScopes.Release).CompleteContainerItemIds));
            Assert.Equal(sibling.Planning!.AcceptanceCriteria, configured.ReleasePlanning!.AcceptanceCriteria);
            Assert.DoesNotContain(configured.TaskAssignments, a => a.ItemId == task.Id);
            Assert.NotEqual(release.SourceBranch, configured.Branches.Single(b => b.Scope == WorkExecutionScopes.Release).SourceBranch);
        }
    }
}
