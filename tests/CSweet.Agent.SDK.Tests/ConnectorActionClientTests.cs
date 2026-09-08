using System.Text.Json;

namespace CSweet.Agent.SDK.Tests;

public sealed class ConnectorActionClientTests
{
    [Fact]
    public async Task MediaSourceIsPreservedWithoutBecomingAnAccessToken()
    {
        var source = new ConversationAttachmentReference(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var action = new ConnectorAction(Guid.NewGuid(), "example.asset.publish.v1", "AwaitingApproval", DateTimeOffset.UtcNow);
        var request = new RequestConnectorAction(action.Capability, JsonSerializer.SerializeToElement(new { assetId = Guid.NewGuid() }), "media-key")
            { MediaSource = source };
        var runtime = new AgentTestRuntime().RegisterCapability<RequestConnectorAction, ConnectorAction>(
            PlatformCapabilities.ConnectorActionRequest, (received, _) =>
            {
                Assert.Equal(source, received.MediaSource);
                return Task.FromResult(action);
            });
        Assert.Equal(action, await runtime.CreateContext().Platform.Connectors.RequestActionAsync(request));
        var json = JsonSerializer.SerializeToElement(request with { MediaSource = null }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.False(json.TryGetProperty("mediaSource", out _));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task IncompleteMediaSourcesFailBeforeInvokingTheHost(int emptyField)
    {
        var source = new ConversationAttachmentReference(emptyField == 0 ? Guid.Empty : Guid.NewGuid(),
            emptyField == 1 ? Guid.Empty : Guid.NewGuid(), emptyField == 2 ? Guid.Empty : Guid.NewGuid());
        var request = new RequestConnectorAction("example.asset.publish.v1", JsonSerializer.SerializeToElement(new { }), "key")
            { MediaSource = source };
        await Assert.ThrowsAsync<ArgumentException>(() => new AgentTestRuntime().CreateContext().Platform.Connectors.RequestActionAsync(request));
    }

    [Fact]
    public async Task CancellationUsesItsOwnGrantAndDecisionFeedbackSurvivesRead()
    {
        var action = new ConnectorAction(Guid.NewGuid(), "example.item.update.v1", "RevisionRequested", DateTimeOffset.UtcNow,
            Decision: new("RequestRevision", "Use a friendlier reply.", DateTimeOffset.UtcNow));
        var runtime = new AgentTestRuntime()
            .RegisterCapability<ReadConnectorAction, ConnectorAction>(PlatformCapabilities.ConnectorActionRead, (r, _) => Task.FromResult(action))
            .RegisterCapability<CancelConnectorAction, ConnectorAction>(PlatformCapabilities.ConnectorActionCancel, (r, _) =>
            {
                Assert.Equal(action.ActionId, r.ActionId); Assert.Equal("cancel-once", r.IdempotencyKey);
                return Task.FromResult(action with { Status = "Cancelled", Decision = null });
            });
        var client = runtime.CreateContext().Platform.Connectors;
        Assert.Equal(action.Decision, (await client.ReadActionAsync(new(action.ActionId))).Decision);
        Assert.Equal("Cancelled", (await client.CancelActionAsync(new(action.ActionId, "cancel-once"))).Status);
        await Assert.ThrowsAsync<PlatformCapabilityException>(() => new AgentTestRuntime().CreateContext().Platform.Connectors
            .CancelActionAsync(new(action.ActionId, "cancel-once")));
    }
    [Fact]
    public async Task RequestAndReadPreserveDomainKeyWithoutCallerSelectedAuthority()
    {
        var action = new ConnectorAction(Guid.NewGuid(), "example.item.update.v1", "AwaitingApproval", DateTimeOffset.UtcNow);
        var input = JsonSerializer.SerializeToElement(new { itemId = "item", title = "Reviewed title" });
        var runtime = new AgentTestRuntime()
            .RegisterCapability<RequestConnectorAction, ConnectorAction>(PlatformCapabilities.ConnectorActionRequest, (request, _) =>
            {
                Assert.Equal(action.Capability, request.Capability); Assert.Equal("stable-key", request.IdempotencyKey);
                Assert.True(JsonElement.DeepEquals(input, request.Input)); return Task.FromResult(action);
            })
            .RegisterCapability<ReadConnectorAction, ConnectorAction>(PlatformCapabilities.ConnectorActionRead, (request, _) =>
            {
                Assert.Equal(action.ActionId, request.ActionId); return Task.FromResult(action with { Status = "Completed" });
            });
        var client = runtime.CreateContext().Platform.Connectors;
        Assert.Equal(action, await client.RequestActionAsync(new(action.Capability, input, "stable-key")));
        Assert.Equal("Completed", (await client.ReadActionAsync(new(action.ActionId))).Status);
    }

    [Theory]
    [InlineData("")]
    [InlineData("line\nbreak")]
    public async Task InvalidActionKeysFailBeforeInvokingTheHost(string key)
    {
        var client = new AgentTestRuntime().CreateContext().Platform.Connectors;
        await Assert.ThrowsAsync<ArgumentException>(() => client.RequestActionAsync(new("example.update.v1",
            JsonSerializer.SerializeToElement(new { }), key)));
        await Assert.ThrowsAsync<ArgumentException>(() => client.ReadActionAsync(new(Guid.Empty)));
        await Assert.ThrowsAsync<ArgumentException>(() => client.CancelActionAsync(new(Guid.NewGuid(), key)));
        await Assert.ThrowsAsync<ArgumentException>(() => client.CancelActionAsync(new(Guid.Empty, "valid")));
    }
}
