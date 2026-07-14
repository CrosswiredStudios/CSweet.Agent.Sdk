using System.Runtime.CompilerServices;
using System.Text.Json;
using CSweet.Agent.Contracts.Grpc;
using Google.Protobuf;
using Microsoft.Extensions.AI;

namespace CSweet.Agent.SDK;

public static class BrokerLlmCapabilities
{
    public const string ChatStream = "platform.llm.chat-stream.v1";
}

public sealed record BrokerLlmMessage(string Role, string Text);

public sealed record BrokerLlmRequest(
    Guid ProviderProfileId,
    string? Model,
    IReadOnlyList<BrokerLlmMessage> Messages);

public sealed record BrokerLlmChunk(
    string? Text,
    long? InputTokenCount = null,
    long? OutputTokenCount = null);

public sealed class BrokerLlmClient : IChatClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IAgentBrokerClient _broker;
    private readonly AgentLlmSelection _selection;

    public BrokerLlmClient(IAgentBrokerClient broker, AgentLlmSelection selection)
    {
        _broker = broker;
        _selection = selection;
    }

    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var text = new System.Text.StringBuilder();
        await foreach (var update in GetStreamingResponseAsync(messages, options, cancellationToken))
        {
            text.Append(update.Text);
        }

        return new ChatResponse(new ChatMessage(ChatRole.Assistant, text.ToString()));
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var payload = new BrokerLlmRequest(
            _selection.ProviderProfileId,
            _selection.Model,
            messages.Select(message => new BrokerLlmMessage(
                message.Role.ToString(),
                message.Text ?? string.Empty)).ToList());
        var request = new RequestCapability
        {
            RequestId = Guid.NewGuid().ToString("N"),
            Capability = BrokerLlmCapabilities.ChatStream,
            ContentType = "application/json",
            Payload = ByteString.CopyFrom(JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions))
        };

        await foreach (var result in _broker.InvokeStreamingCapabilityAsync(
            request,
            request.RequestId,
            cancellationToken))
        {
            if (!result.Succeeded)
            {
                throw new InvalidOperationException(
                    string.IsNullOrWhiteSpace(result.Error)
                        ? "The platform LLM capability failed."
                        : result.Error);
            }

            if (result.Payload.IsEmpty)
            {
                continue;
            }

            var chunk = JsonSerializer.Deserialize<BrokerLlmChunk>(
                result.Payload.Span,
                JsonOptions);
            if (chunk is null)
            {
                continue;
            }

            if (!string.IsNullOrEmpty(chunk.Text))
            {
                yield return new ChatResponseUpdate(ChatRole.Assistant, chunk.Text);
            }

            if (chunk.InputTokenCount is not null || chunk.OutputTokenCount is not null)
            {
                yield return new ChatResponseUpdate(ChatRole.Assistant, [
                    new UsageContent(new UsageDetails
                    {
                        InputTokenCount = chunk.InputTokenCount,
                        OutputTokenCount = chunk.OutputTokenCount
                    })
                ]);
            }
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceType.IsInstanceOfType(this) ? this : null;

    public void Dispose()
    {
    }
}
