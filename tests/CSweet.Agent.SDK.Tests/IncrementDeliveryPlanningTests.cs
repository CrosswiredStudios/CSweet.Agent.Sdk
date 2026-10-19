using System.Text.Json;
using CSweet.WorkManagement.Contracts;

namespace CSweet.Agent.SDK.Tests;

public sealed class IncrementDeliveryPlanningTests
{
    [Fact]
    public async Task SuccessorIncrementTargetsSharedInternalBranchAndRetainsFinalReleaseAsSeparateBoundary()
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
                items[2] = task with { Delivery = r.Delivery, StageAssignments = r.StageAssignments }; return Task.FromResult(items[2]); })
            .RegisterCapability<ControlWorkDeliveryPlanRequest, WorkDeliveryPlanResponse>(WorkDeliveryCapabilities.Control,
                (r, _) => Task.FromResult(plans.Single(p => p.Id == r.PlanId) with { Status = "Active" }));
        var result = await HierarchicalProjectDelivery.PrepareIncrementAsync(project, boardId, manager, roster, repository,
            "main", new string('a', 64), false, [epic.Id], runtime.CreateContext(), default);
        Assert.Equal("Active", result.Status);
        var release = Assert.Single(configured!.Branches, b => b.Scope == WorkExecutionScopes.Release);
        Assert.Equal($"csweet/integration/{project:N}", release.TargetBranch);
        Assert.DoesNotContain(configured.Branches, b => b.TargetBranch == "main");
        Assert.Equal(release.SourceBranch, Assert.Single(configured.Branches, b => b.Scope == WorkExecutionScopes.Story).TargetBranch);
        Assert.Equal(configured.EpicItemIds, new[] { epic.Id });
        Assert.Contains(configured.Assignments.Single(a => a.Scope == WorkExecutionScopes.Release).Stages, s => s.StageKey == "quality");
        Assert.Contains(configured.Assignments.Single(a => a.Scope == WorkExecutionScopes.Release).Stages, s => s.StageKey == "technical-review");
    }
}
