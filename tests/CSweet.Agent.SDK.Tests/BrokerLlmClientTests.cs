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
        var broker = new StreamingBrokerClient(
            new BrokerLlmChunk("first "),
            new BrokerLlmChunk("second"));
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

    [Fact]
    public async Task GetStreamingResponseAsync_TransportsInstructionsToolsAndFunctionCalls()
    {
        using var schemaDocument = JsonDocument.Parse("""
            {"type":"object","properties":{"question":{"type":"string"}},"required":["question"]}
            """);
        var broker = new StreamingBrokerClient(new BrokerLlmChunk(
            null,
            Role: "assistant",
            Contents:
            [
                new BrokerLlmContent(
                    "function_call",
                    CallId: "call-1",
                    Name: "ask_user",
                    Arguments: new Dictionary<string, JsonElement>
                    {
                        ["question"] = JsonSerializer.SerializeToElement("Which role should we hire first?")
                    })
            ]));
        using var client = new BrokerLlmClient(
            broker,
            new AgentLlmSelection(Guid.NewGuid(), "model"));
        var calls = new List<FunctionCallContent>();

        await foreach (var update in client.GetStreamingResponseAsync(
            [
                new ChatMessage(ChatRole.User, "Help me staff the company"),
                new ChatMessage(ChatRole.Assistant,
                [
                    new FunctionCallContent(
                        "previous-call",
                        "list_hiring_recommendations",
                        new Dictionary<string, object?>())
                ]),
                new ChatMessage(ChatRole.Tool,
                [
                    new FunctionResultContent(
                        "previous-call",
                        new { recommendations = Array.Empty<object>() })
                ])
            ],
            new ChatOptions
            {
                Instructions = "Recommend roles only.",
                Tools =
                [
                    AIFunctionFactory.CreateDeclaration(
                        "ask_user",
                        "Ask the user to choose an option.",
                        schemaDocument.RootElement.Clone())
                ]
            }))
        {
            calls.AddRange(update.Contents.OfType<FunctionCallContent>());
        }

        var request = Assert.IsType<BrokerLlmRequest>(broker.Request);
        Assert.Equal("Recommend roles only.", request.Instructions);
        var tool = Assert.Single(request.Tools!);
        Assert.Equal("ask_user", tool.Name);
        Assert.Equal("object", tool.JsonSchema.GetProperty("type").GetString());
        Assert.Contains(
            request.Messages.SelectMany(message => message.Contents ?? []),
            content => content.Kind == "function_call" && content.CallId == "previous-call");
        Assert.Contains(
            request.Messages.SelectMany(message => message.Contents ?? []),
            content => content.Kind == "function_result" && content.CallId == "previous-call");
        var call = Assert.Single(calls);
        Assert.Equal("call-1", call.CallId);
        Assert.Equal("ask_user", call.Name);
        Assert.Equal("Which role should we hire first?", ((JsonElement)call.Arguments!["question"]!).GetString());
    }

    private sealed class StreamingBrokerClient(params BrokerLlmChunk[] chunks) : IAgentBrokerClient
    {
        public string? RequestedCapability { get; private set; }
        public BrokerLlmRequest? Request { get; private set; }

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
            Request = JsonSerializer.Deserialize<BrokerLlmRequest>(
                request.Payload.Span,
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
            for (var index = 0; index < chunks.Length; index++)
            {
                await Task.Yield();
                yield return new CapabilityResult
                {
                    RequestId = request.RequestId,
                    Succeeded = true,
                    ContentType = "application/json",
                    Payload = ByteString.CopyFrom(JsonSerializer.SerializeToUtf8Bytes(chunks[index])),
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
