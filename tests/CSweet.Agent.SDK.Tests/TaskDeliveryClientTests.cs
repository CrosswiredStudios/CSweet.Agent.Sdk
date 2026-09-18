using CSweet.Agent.SDK;
namespace CSweet.Agent.SDK.Tests;
public sealed class TaskDeliveryClientTests
{
    [Fact]
    public async Task Review_discovery_is_distinct_from_current_task_read_and_preserves_exact_source()
    {
        var item = Guid.NewGuid(); var root = Guid.NewGuid();
        var review = new TaskReviewResult(Guid.NewGuid(), item, root, Guid.NewGuid(), "Task", "Scope", ["Works"], new string('a', 40), "Testing", "Pending", null, Guid.NewGuid(), 1, 2);
        var runtime = new AgentTestRuntime()
            .RegisterCapability<object, IReadOnlyList<TaskReviewResult>>(TaskDeliveryCapabilities.List, (_, _) => Task.FromResult<IReadOnlyList<TaskReviewResult>>([review]))
            .RegisterCapability<ReadTaskReviewRequest, TaskReviewResult>(TaskDeliveryCapabilities.Read, (r, _) => { Assert.Equal(item, r.TaskItemId); return Task.FromResult(review); });
        var client = runtime.CreateContext().Platform.SourceControl;
        Assert.Equal(review.Id, Assert.Single(await client.ListTaskReviewsAsync()).Id);
        var current = await client.ReadTaskReviewAsync(new(item));
        Assert.Equal(review.CommitSha, current.CommitSha); Assert.Equal(review.AcceptanceCriteria, current.AcceptanceCriteria);
        foreach (var name in new[] { TaskDeliveryCapabilities.List, TaskDeliveryCapabilities.Read, TaskDeliveryCapabilities.Submit, TaskDeliveryCapabilities.Decide,
            TaskDeliveryCapabilities.Preferences, TaskDeliveryCapabilities.ChangePreference, TaskDeliveryCapabilities.Quality }) Assert.True(CapabilityCatalog.IsKnown(name));
    }
}
