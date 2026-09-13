using CSweet.Agent.SDK;
namespace CSweet.Agent.SDK.Tests;
public sealed class PersonalGitWorkspaceTests
{
    [Fact]
    public async Task Personal_workspace_request_contains_only_the_owned_ticket_and_stable_key()
    {
        var item = Guid.NewGuid();
        var runtime = new AgentTestRuntime().RegisterCapability<PreparePersonalGitWorkspaceRequest, GitWorkspaceResult>(
            GitWorkspaceCapabilities.PreparePersonal, (request, _) => {
                Assert.Equal(item, request.ItemId); Assert.Equal("personal:prepare", request.IdempotencyKey);
                return Task.FromResult(new GitWorkspaceResult(Guid.NewGuid(), item, "/workspace/ticket/1", Guid.NewGuid(),
                    "InternalGit", "PullRequest", new string('a', 40), "Ready", false));
            });
        var result = await runtime.CreateContext().Platform.Git.PreparePersonalAsync(new(item, "personal:prepare"));
        Assert.Equal(item, result.WorkItemId); Assert.Equal("InternalGit", result.Provider);
    }
}
