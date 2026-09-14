using CSweet.WorkManagement.Contracts;

namespace CSweet.Agent.SDK.Tests;

public sealed class PersonalWorkPlanningClientTests
{
    [Fact]
    public async Task PlanAndProgressUseTypedCapabilitiesAndRetainTheUpdatedClaimRevision()
    {
        var id = Guid.NewGuid();
        var task = new PersonalTodoItem(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Daniel",
            "Rules", "Implement rules", "Running", "Medium", 0, 2, null, null, null, [], null, null,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow)
            { Kind = "Task", ParentItemId = Guid.NewGuid(), PlanRootId = id, PlanExecution = "Implementation", AcceptanceCriteria = ["Rules pass"] };
        CreatePersonalWorkPlanRequest? created = null;
        ReportPersonalWorkPlanTaskRequest? reported = null;
        var runtime = new AgentTestRuntime()
            .RegisterCapability<CreatePersonalWorkPlanRequest, PersonalWorkPlan>(PersonalWorkPlanCapabilities.Create,
                (r, _) => { created = r; return Task.FromResult(new PersonalWorkPlan(id, 5, [task])); })
            .RegisterCapability<ReportPersonalWorkPlanTaskRequest, PersonalTodoItem>(PersonalWorkPlanCapabilities.ReportTask,
                (r, _) => { reported = r; return Task.FromResult(task with { Status = r.Status }); });
        var client = runtime.CreateContext().Platform.PersonalTodo;
        Assert.Equal(4, client.ClaimedRevision(id, 4));
        var plan = await client.CreatePlanAsync(new(id, "Tetris MVP", [], "plan"));
        Assert.Equal("Tetris MVP", created!.EpicTitle);
        Assert.Equal(task.ParentItemId, plan.Items.Single().ParentItemId);
        Assert.Equal(task.AcceptanceCriteria, plan.Items.Single().AcceptanceCriteria);
        Assert.Equal(5, client.ClaimedRevision(id, 4));
        Assert.Equal(8, client.ClaimedRevision(id, 8)); // A later claim must never use an older cached revision.
        Assert.Equal(1, client.ClaimedRevision(Guid.NewGuid(), 1));
        var completed = await client.ReportPlanTaskAsync(new(id, task.Id, 2, "Completed", "Tests passed", "done"));
        Assert.Equal(id, reported!.RootItemId);
        Assert.Equal("Tests passed", reported.Evidence);
        Assert.Equal("Completed", completed.Status);
    }
}
