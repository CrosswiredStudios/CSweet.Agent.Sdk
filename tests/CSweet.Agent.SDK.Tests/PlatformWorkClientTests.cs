using CSweet.Agent.SDK;
using CSweet.WorkManagement.Contracts;

namespace CSweet.Agent.SDK.Tests;

public sealed class PlatformWorkClientTests
{
    [Fact]
    public async Task CreateItemAsync_PreservesAccountabilityAndExactAssignments()
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

        var ownerId = Guid.NewGuid();
        var installationId = Guid.NewGuid();
        var result = await context.Platform.Work.CreateItemAsync(new CreateWorkItemRequest(
            boardId, "Reconcile invoices", "Close the monthly books.", WorkItemKinds.Task,
            WorkPriorities.High, null, null, null, "task-1")
        {
            AccountableOrganizationUserId = ownerId,
            StageAssignments = [new("development", WorkOrchestrationPrincipalKinds.AgentInstallation,
                AgentInstallationId: installationId)]
        });

        Assert.Equal(itemId, result.Id);
        Assert.NotNull(captured);
        Assert.Equal(boardId, captured.BoardId);
        Assert.Equal(WorkItemKinds.Task, captured.Kind);
        Assert.Equal(WorkPriorities.High, captured.Priority);
        Assert.Equal("task-1", captured.IdempotencyKey);
        Assert.Equal(ownerId, captured.AccountableOrganizationUserId);
        Assert.Equal(installationId, Assert.Single(captured.StageAssignments).AgentInstallationId);
    }

    [Fact]
    public async Task CommentAsync_UsesNonTransitioningCapability()
    {
        CommentOnWorkItemRequest? captured = null;
        var boardId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var runtime = new AgentTestRuntime()
            .RegisterCapability<CommentOnWorkItemRequest, WorkItemComment>(
                WorkItemCapabilities.Comment,
                (request, _) =>
                {
                    captured = request;
                    return Task.FromResult(new WorkItemComment(
                        Guid.NewGuid(), request.ItemId, "Agent", Guid.NewGuid(), "Agent",
                        request.Body, 1, DateTimeOffset.UtcNow, null));
                });

        var result = await runtime.CreateContext().Platform.Work.CommentAsync(
            new CommentOnWorkItemRequest(boardId, itemId, "Progress", "comment-1"));

        Assert.Equal("Progress", result.Body);
        Assert.Equal(boardId, captured!.BoardId);
        Assert.Equal(itemId, captured.ItemId);
        Assert.Equal("comment-1", captured.IdempotencyKey);
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
            WorkItemCapabilities.Comment,
            WorkSprintCapabilities.Read,
            WorkSprintCapabilities.ReadReports,
            WorkOrchestrationCapabilities.Start,
            WorkOrchestrationCapabilities.Execute
        };

        Assert.All(capabilities, capability =>
            Assert.True(CapabilityCatalog.IsKnown(capability)));
        Assert.False(CapabilityCatalog.IsKnown(WorkItemCapabilities.Complete));
    }
}
