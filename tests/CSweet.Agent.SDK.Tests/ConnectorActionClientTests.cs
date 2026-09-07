using System.Text.Json;

namespace CSweet.Agent.SDK.Tests;

public sealed class ConnectorActionClientTests
{
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
    }
}
