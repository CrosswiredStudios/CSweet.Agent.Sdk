using CSweet.Agent.SDK;

namespace CSweet.Agent.SDK.Tests;

public sealed class PlatformGitWorkspaceClientTests
{
    [Fact]
    public async Task FileLocksPreserveAssignmentAndExposeOwnershipWithoutCredentials()
    {
        var workspace = Guid.NewGuid(); var lockId = Guid.NewGuid().ToString("N");
        var fileLock = new GitWorkspaceFileLock(lockId, "art/asset.bin", "Developer", true, DateTimeOffset.UtcNow);
        var runtime = new AgentTestRuntime()
            .RegisterCapability<LockGitWorkspaceFileRequest, GitWorkspaceLockResult>(GitWorkspaceCapabilities.LockFile, (request, _) =>
            {
                Assert.Equal(workspace, request.WorkspaceId); Assert.Equal(7, request.AssignmentRevision);
                Assert.Equal("art/asset.bin", request.Path); Assert.Equal("lock:asset", request.IdempotencyKey);
                return Task.FromResult(new GitWorkspaceLockResult("Locked", [fileLock]));
            })
            .RegisterCapability<ListGitWorkspaceLocksRequest, GitWorkspaceLockResult>(GitWorkspaceCapabilities.ListLocks, (request, _) =>
            {
                Assert.Equal(workspace, request.WorkspaceId); Assert.Equal(lockId, request.Cursor);
                return Task.FromResult(new GitWorkspaceLockResult("Listed", [fileLock]));
            })
            .RegisterCapability<UnlockGitWorkspaceFileRequest, GitWorkspaceLockResult>(GitWorkspaceCapabilities.UnlockFile, (request, _) =>
            {
                Assert.Equal(workspace, request.WorkspaceId); Assert.Equal(lockId, request.LockId);
                Assert.Equal("unlock:asset", request.IdempotencyKey);
                return Task.FromResult(new GitWorkspaceLockResult("Unlocked", []));
            });
        var git = runtime.CreateContext().Platform.Git;
        Assert.True(Assert.Single((await git.LockFileAsync(new(workspace, 7, "art/asset.bin", "lock:asset"))).Locks).OwnedByCaller);
        Assert.Equal(fileLock, Assert.Single((await git.ListLocksAsync(new(workspace, 7, lockId))).Locks));
        Assert.Equal("Unlocked", (await git.UnlockFileAsync(new(workspace, 7, lockId, "unlock:asset"))).Status);
    }

    [Fact]
    public async Task PublishAsync_PreservesBoundedValidationEvidence()
    {
        PublishGitWorkspaceRequest? captured = null;
        var workspaceId = Guid.NewGuid();
        var publicationId = Guid.NewGuid();
        var repositoryId = Guid.NewGuid();
        var runtime = new AgentTestRuntime()
            .RegisterCapability<PublishGitWorkspaceRequest, GitWorkspacePublication>(
                GitWorkspaceCapabilities.Publish,
                (request, _) =>
                {
                    captured = request;
                    return Task.FromResult(new GitWorkspacePublication(
                        publicationId,
                        request.WorkspaceId,
                        repositoryId,
                        "GitHub",
                        GitDeliveryKinds.PullRequest,
                        "csweet/ticket",
                        "0123456789abcdef",
                        new Uri("https://github.com/example/repository/pull/1"),
                        "Published"));
                });

        var result = await runtime.CreateContext().Platform.Git.PublishAsync(
            new PublishGitWorkspaceRequest(
                workspaceId,
                7,
                "Implement ticket",
                "Ticket",
                "Evidence",
                "event:publish",
                [new GitValidationResult("dotnet test", true, 0)]));

        Assert.Equal(GitDeliveryKinds.PullRequest, result.DeliveryKind);
        var validation = Assert.Single(captured!.Validations!);
        Assert.Equal("dotnet test", validation.Command);
        Assert.True(validation.Succeeded);
        Assert.Equal(0, validation.ExitCode);
    }

    [Fact]
    public void GitWorkspaceCapabilities_AreKnownAndVersioned()
    {
        var capabilities = new[]
        {
            GitWorkspaceCapabilities.Prepare,
            GitWorkspaceCapabilities.Refresh,
            GitWorkspaceCapabilities.Inspect,
            GitWorkspaceCapabilities.Publish,
            GitWorkspaceCapabilities.Cleanup,
            GitWorkspaceCapabilities.ListLocks,
            GitWorkspaceCapabilities.LockFile,
            GitWorkspaceCapabilities.UnlockFile
        };

        Assert.All(capabilities, capability =>
            Assert.True(CapabilityCatalog.IsKnown(capability)));
        Assert.All(capabilities, capability =>
            Assert.EndsWith(".v2", capability, StringComparison.Ordinal));
    }
}
