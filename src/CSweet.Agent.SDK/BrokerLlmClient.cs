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

public sealed record BrokerLlmContent(
    string Kind,
    string? Text = null,
    string? CallId = null,
    string? Name = null,
    IReadOnlyDictionary<string, JsonElement>? Arguments = null,
    JsonElement? Result = null);

public sealed record BrokerLlmMessage(
    string Role,
    string? Text = null,
    IReadOnlyList<BrokerLlmContent>? Contents = null);

public sealed record BrokerLlmTool(
    string Name,
    string Description,
    JsonElement JsonSchema);

public sealed record BrokerLlmRequest(
    Guid ProviderProfileId,
    string? Model,
    IReadOnlyList<BrokerLlmMessage> Messages,
    string? Instructions = null,
    IReadOnlyList<BrokerLlmTool>? Tools = null);

public sealed record BrokerLlmChunk(
    string? Text,
    long? InputTokenCount = null,
    long? OutputTokenCount = null,
    string? Role = null,
    IReadOnlyList<BrokerLlmContent>? Contents = null);

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
        var contents = new List<AIContent>();
        await foreach (var update in GetStreamingResponseAsync(messages, options, cancellationToken))
        {
            contents.AddRange(update.Contents.Where(content => content is not UsageContent));
        }

        return new ChatResponse(new ChatMessage(ChatRole.Assistant, contents));
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var payload = new BrokerLlmRequest(
            _selection.ProviderProfileId,
            _selection.Model,
            messages.Select(ToBrokerMessage).ToList(),
            options?.Instructions,
            options?.Tools?
                .OfType<AIFunctionDeclaration>()
                .Select(tool => new BrokerLlmTool(
                    tool.Name,
                    tool.Description,
                    tool.JsonSchema.Clone()))
                .ToList());
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

            var role = ParseRole(chunk.Role);
            var contents = chunk.Contents?.Select(ToAiContent).ToList() ?? [];
            if (!string.IsNullOrEmpty(chunk.Text) && contents.All(content => content is not TextContent))
            {
                contents.Insert(0, new TextContent(chunk.Text));
            }

            if (contents.Count > 0)
            {
                yield return new ChatResponseUpdate(role, contents);
            }

            if (chunk.InputTokenCount is not null || chunk.OutputTokenCount is not null)
            {
                yield return new ChatResponseUpdate(role, [
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

    private static BrokerLlmMessage ToBrokerMessage(ChatMessage message) => new(
        message.Role.ToString(),
        message.Text,
        message.Contents.Select(ToBrokerContent).ToList());

    private static BrokerLlmContent ToBrokerContent(AIContent content) => content switch
    {
        TextContent text => new BrokerLlmContent("text", Text: text.Text),
        FunctionCallContent call => new BrokerLlmContent(
            "function_call",
            CallId: call.CallId,
            Name: call.Name,
            Arguments: call.Arguments?.ToDictionary(
                argument => argument.Key,
                argument => SerializeElement(argument.Value),
                StringComparer.Ordinal)),
        FunctionResultContent result => new BrokerLlmContent(
            "function_result",
            CallId: result.CallId,
            Result: SerializeElement(result.Result)),
        _ => throw new NotSupportedException(
            $"Brokered LLM messages do not support {content.GetType().Name} content.")
    };

    private static AIContent ToAiContent(BrokerLlmContent content) => content.Kind switch
    {
        "text" => new TextContent(content.Text ?? string.Empty),
        "function_call" when !string.IsNullOrWhiteSpace(content.CallId) &&
            !string.IsNullOrWhiteSpace(content.Name) => new FunctionCallContent(
                content.CallId,
                content.Name,
                content.Arguments?.ToDictionary(
                    argument => argument.Key,
                    argument => (object?)argument.Value.Clone(),
                    StringComparer.Ordinal) ?? new Dictionary<string, object?>()),
        "function_result" when !string.IsNullOrWhiteSpace(content.CallId) =>
            new FunctionResultContent(content.CallId, content.Result?.Clone()),
        _ => throw new InvalidOperationException(
            $"The broker returned unsupported or incomplete '{content.Kind}' content.")
    };

    private static JsonElement SerializeElement(object? value) =>
        value is JsonElement element
            ? element.Clone()
            : JsonSerializer.SerializeToElement(value, value?.GetType() ?? typeof(object), JsonOptions);

    private static ChatRole ParseRole(string? role) => role?.ToLowerInvariant() switch
    {
        "system" => ChatRole.System,
        "user" => ChatRole.User,
        "tool" => ChatRole.Tool,
        _ => ChatRole.Assistant
    };
}
