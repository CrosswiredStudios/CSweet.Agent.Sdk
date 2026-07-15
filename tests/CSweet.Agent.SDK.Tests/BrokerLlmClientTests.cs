using System.Text.Json;
using CSweet.Agent.Contracts.Grpc;
using Google.Protobuf;
using Microsoft.Extensions.AI;

namespace CSweet.Agent.SDK.Tests;

public sealed class BrokerLlmClientTests
{
    [Fact]
    public async Task GetStreamingResponseAsync_YieldsCorrelatedBrokerChunks()
    {
        var broker = new StreamingBrokerClient("first ", "second");
        using var client = new BrokerLlmClient(
            broker,
            new AgentLlmSelection(Guid.NewGuid(), "model"));
        var text = new List<string>();

        await foreach (var update in client.GetStreamingResponseAsync([
            new ChatMessage(ChatRole.User, "hello")
        ]))
        {
            if (!string.IsNullOrEmpty(update.Text))
            {
                text.Add(update.Text);
            }
        }

        Assert.Equal(["first ", "second"], text);
        Assert.Equal(BrokerLlmCapabilities.ChatStream, broker.RequestedCapability);
    }

    private sealed class StreamingBrokerClient(params string[] chunks) : IAgentBrokerClient
    {
        public string? RequestedCapability { get; private set; }

        public Task StartAsync(RegisterAgent registration, CancellationToken cancellationToken) => Task.CompletedTask;
        public async IAsyncEnumerable<BrokerToAgentMessage> ReadAllAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
            yield break;
        }
        public Task PublishEventAsync(PublishEvent message, string? correlationId = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<CapabilityResult> InvokeCapabilityAsync(RequestCapability request, string? correlationId = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public async IAsyncEnumerable<CapabilityResult> InvokeStreamingCapabilityAsync(RequestCapability request, string? correlationId = null, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            RequestedCapability = request.Capability;
            for (var index = 0; index < chunks.Length; index++)
            {
                await Task.Yield();
                yield return new CapabilityResult
                {
                    RequestId = request.RequestId,
                    Succeeded = true,
                    ContentType = "application/json",
                    Payload = ByteString.CopyFrom(JsonSerializer.SerializeToUtf8Bytes(new BrokerLlmChunk(chunks[index]))),
                    Sequence = index,
                    HasMore = index < chunks.Length - 1
                };
            }
        }
        public Task SendCapabilityResultAsync(CapabilityResult result, string? correlationId = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
