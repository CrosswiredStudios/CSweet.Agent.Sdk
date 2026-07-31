using CSweet.Agent.SDK;

namespace CSweet.Agent.SDK.Tests;

public sealed class PlatformGitWorkspaceClientTests
{
    [Fact]
    public async Task PublishAsync_PreservesBoundedValidationEvidence()
    {
        PublishGitWorkspaceRequest? captured = null;
        var workspaceId = Guid.NewGuid();
        var runtime = new AgentTestRuntime()
            .RegisterCapability<PublishGitWorkspaceRequest, GitWorkspacePublication>(
                GitWorkspaceCapabilities.Publish,
                (request, _) =>
                {
                    captured = request;
                    return Task.FromResult(new GitWorkspacePublication(
                        request.WorkspaceId,
                        "csweet/ticket",
                        "0123456789abcdef",
                        true,
                        new Uri("https://github.com/example/repository/pull/1"),
                        "Published"));
                });

        var result = await runtime.CreateContext().Platform.Git.PublishAsync(
            new PublishGitWorkspaceRequest(
                workspaceId,
                "Implement ticket",
                "Ticket",
                "Evidence",
                "event:publish",
                [new GitValidationResult("dotnet test", true, 0)]));

        Assert.True(result.Pushed);
        Assert.Equal(
            CSweet.WorkManagement.Contracts.DeliveryMergeStatuses.None,
            result.MergeStatus);
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
            GitWorkspaceCapabilities.Inspect,
            GitWorkspaceCapabilities.Publish,
            GitWorkspaceCapabilities.Cleanup
        };

        Assert.All(capabilities, capability =>
            Assert.True(CapabilityCatalog.IsKnown(capability)));
        Assert.All(capabilities, capability =>
            Assert.EndsWith(".v1", capability, StringComparison.Ordinal));
    }
}
