using CSweet.Agent.SDK;
namespace CSweet.Agent.SDK.Tests;
public sealed class PersonalGitWorkspaceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Personal_workspace_preserves_optional_source_task_without_repository_or_commit_selection(bool followUp)
    {
        var item = Guid.NewGuid(); var child = Guid.NewGuid();
        Guid? source = followUp ? Guid.NewGuid() : null;
        var runtime = new AgentTestRuntime().RegisterCapability<PreparePersonalGitWorkspaceRequest, GitWorkspaceResult>(
            GitWorkspaceCapabilities.PreparePersonal, (request, _) => {
                Assert.Equal(source, request.SourceWorkItemId); Assert.Equal(child, request.TaskItemId);
                Assert.Equal(item, request.ItemId); Assert.Equal("personal:prepare", request.IdempotencyKey);
                return Task.FromResult(new GitWorkspaceResult(Guid.NewGuid(), item, "/workspace/ticket/1", Guid.NewGuid(),
                    "InternalGit", "PullRequest", new string('a', 40), "Ready", false));
            });
        var result = await runtime.CreateContext().Platform.Git.PreparePersonalAsync(new(item, "personal:prepare") { SourceWorkItemId = source, TaskItemId = child });
        Assert.Equal(item, result.WorkItemId); Assert.Equal("InternalGit", result.Provider);
    }
}
