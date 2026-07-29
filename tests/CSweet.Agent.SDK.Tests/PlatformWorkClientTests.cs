using CSweet.Agent.SDK;
using CSweet.WorkManagement.Contracts;

namespace CSweet.Agent.SDK.Tests;

public sealed class PlatformWorkClientTests
{
    [Fact]
    public async Task CreateTaskAsync_UsesTypedItemCapabilityAndDefaults()
    {
        CreateWorkItemRequest? captured = null;
        var itemId = Guid.NewGuid();
        var columnId = Guid.NewGuid();
        var runtime = new AgentTestRuntime()
            .RegisterCapability<CreateWorkItemRequest, WorkItem>(
                WorkItemCapabilities.Create,
                (request, _) =>
                {
                    captured = request;
                    return Task.FromResult(new WorkItem(
                        itemId, columnId, null, null, request.Kind,
                        request.Title, request.Description ?? "", "Ready",
                        request.Priority, null, 1024, 1, request.DueDate));
                });
        var context = runtime.CreateContext();
        var boardId = Guid.NewGuid();

        var result = await context.Platform.Work.CreateTaskAsync(
            boardId, "Reconcile invoices", "task-1",
            "Close the monthly books.", WorkPriorities.High);

        Assert.Equal(itemId, result.Id);
        Assert.NotNull(captured);
        Assert.Equal(boardId, captured.BoardId);
        Assert.Equal(WorkItemKinds.Task, captured.Kind);
        Assert.Equal(WorkPriorities.High, captured.Priority);
        Assert.Equal("task-1", captured.IdempotencyKey);
    }

    [Fact]
    public async Task CompleteAsync_UsesCompletionCapabilityAndPreservesConcurrencyFields()
    {
        TransitionWorkItemRequest? captured = null;
        var boardId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var runtime = new AgentTestRuntime()
            .RegisterCapability<TransitionWorkItemRequest, WorkItem>(
                WorkItemCapabilities.Complete,
                (request, _) =>
                {
                    captured = request;
                    return Task.FromResult(new WorkItem(
                        request.ItemId, Guid.NewGuid(), null, null, WorkItemKinds.Task,
                        "Done", "", "Completed", WorkPriorities.Medium,
                        null, 1024, request.ExpectedRevision + 1, null));
                });

        var result = await runtime.CreateContext().Platform.Work.CompleteAsync(
            new TransitionWorkItemRequest(
                boardId, itemId, 7, "complete-1"));

        Assert.Equal("Completed", result.Status);
        Assert.Equal(boardId, captured!.BoardId);
        Assert.Equal(itemId, captured.ItemId);
        Assert.Equal(7, captured.ExpectedRevision);
        Assert.Equal("complete-1", captured.IdempotencyKey);
    }

    [Fact]
    public void WorkCapabilities_AreCanonicalAndManifestEligible()
    {
        var capabilities = new[]
        {
            WorkBoardCapabilities.Read,
            WorkBoardCapabilities.Create,
            WorkItemCapabilities.Read,
            WorkItemCapabilities.Create,
            WorkItemCapabilities.Complete,
            WorkSprintCapabilities.Read,
            WorkSprintCapabilities.ReadReports,
            WorkAutomationCapabilities.Read,
            WorkAutomationCapabilities.Manage
        };

        Assert.All(capabilities, capability =>
            Assert.True(CapabilityCatalog.IsKnown(capability)));
        Assert.Equal("work.item.complete", WorkItemCapabilities.Complete);
    }
}
