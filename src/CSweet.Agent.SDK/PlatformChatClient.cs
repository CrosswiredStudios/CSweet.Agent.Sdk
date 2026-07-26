using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace CSweet.Agent.SDK;

internal static class PlatformChatCapabilities
{
    public const string ChatStream = CapabilityNames.Platform.LlmChatStream;
}

internal sealed record PlatformChatContent(
    string Kind,
    string? Text = null,
    string? CallId = null,
    string? Name = null,
    IReadOnlyDictionary<string, JsonElement>? Arguments = null,
    JsonElement? Result = null);

internal sealed record PlatformChatMessage(
    string Role,
    string? Text = null,
    IReadOnlyList<PlatformChatContent>? Contents = null);

internal sealed record PlatformChatTool(
    string Name,
    string Description,
    JsonElement JsonSchema);

internal sealed record PlatformChatRequest(
    Guid ProviderProfileId,
    string? Model,
    IReadOnlyList<PlatformChatMessage> Messages,
    string? Instructions = null,
    IReadOnlyList<PlatformChatTool>? Tools = null);

internal sealed record PlatformChatChunk(
    string? Text,
    long? InputTokenCount = null,
    long? OutputTokenCount = null,
    string? Role = null,
    IReadOnlyList<PlatformChatContent>? Contents = null);

public sealed class PlatformChatClient : IChatClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IPlatformToolInvoker _tools;
    private readonly AgentLlmSelection _selection;

    public PlatformChatClient(PlatformCapabilityClient platform, AgentLlmSelection selection)
    {
        _tools = platform.Tools;
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
        var payload = new PlatformChatRequest(
            _selection.ProviderProfileId,
            _selection.Model,
            messages.Select(ToBrokerMessage).ToList(),
            options?.Instructions,
            options?.Tools?
                .OfType<AIFunctionDeclaration>()
                .Select(tool => new PlatformChatTool(
                    tool.Name,
                    tool.Description,
                    tool.JsonSchema.Clone()))
                .ToList());
        await foreach (var result in _tools.InvokeStreamingAsync(
            PlatformChatCapabilities.ChatStream,
            JsonSerializer.SerializeToElement(payload, JsonOptions),
            cancellationToken))
        {
            var chunk = result.Deserialize<PlatformChatChunk>(JsonOptions);
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

    private static PlatformChatMessage ToBrokerMessage(ChatMessage message) => new(
        message.Role.ToString(),
        message.Text,
        message.Contents.Select(ToBrokerContent).ToList());

    private static PlatformChatContent ToBrokerContent(AIContent content) => content switch
    {
        TextContent text => new PlatformChatContent("text", Text: text.Text),
        FunctionCallContent call => new PlatformChatContent(
            "function_call",
            CallId: call.CallId,
            Name: call.Name,
            Arguments: call.Arguments?.ToDictionary(
                argument => argument.Key,
                argument => SerializeElement(argument.Value),
                StringComparer.Ordinal)),
        FunctionResultContent result => new PlatformChatContent(
            "function_result",
            CallId: result.CallId,
            Result: SerializeElement(result.Result)),
        _ => throw new NotSupportedException(
            $"Platform chat messages do not support {content.GetType().Name} content.")
    };

    private static AIContent ToAiContent(PlatformChatContent content) => content.Kind switch
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
            $"The platform returned unsupported or incomplete '{content.Kind}' content.")
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
