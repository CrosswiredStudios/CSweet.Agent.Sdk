using CSweet.Agent.SDK;

namespace CSweet.Agent.SDK.Tests;

public sealed class PlatformGitWorkspaceClientTests
{
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
            GitWorkspaceCapabilities.Cleanup
        };

        Assert.All(capabilities, capability =>
            Assert.True(CapabilityCatalog.IsKnown(capability)));
        Assert.All(capabilities, capability =>
            Assert.EndsWith(".v2", capability, StringComparison.Ordinal));
    }
}
