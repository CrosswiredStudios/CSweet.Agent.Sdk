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
    string? ProtectedData = null,
    string? CallId = null,
    string? Name = null,
    IReadOnlyDictionary<string, JsonElement>? Arguments = null,
    JsonElement? Result = null,
    Guid? AttachmentId = null,
    Guid? MessageId = null,
    Guid? ConversationId = null,
    string? FileName = null,
    string? ContentType = null,
    long? SizeBytes = null,
    string? Sha256 = null);

/// <summary>
/// An opaque broker-resolved reference to a retained communication attachment.
/// Agent VMs cannot use this type to obtain storage paths or file bytes.
/// </summary>
public sealed class AgentMediaReferenceContent : AIContent
{
    public AgentMediaReferenceContent(
        Guid attachmentId,
        Guid messageId,
        Guid conversationId,
        string fileName,
        string contentType,
        long sizeBytes,
        string sha256)
    {
        AttachmentId = attachmentId;
        MessageId = messageId;
        ConversationId = conversationId;
        FileName = fileName;
        ContentType = contentType;
        SizeBytes = sizeBytes;
        Sha256 = sha256;
    }

    public Guid AttachmentId { get; }
    public Guid MessageId { get; }
    public Guid ConversationId { get; }
    public string FileName { get; }
    public string ContentType { get; }
    public long SizeBytes { get; }
    public string Sha256 { get; }
}

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
    IReadOnlyList<PlatformChatTool>? Tools = null,
    PlatformChatTelemetry? Telemetry = null,
    ReasoningOutput? ReasoningOutput = null,
    ReasoningEffort? ReasoningEffort = null);

internal sealed record PlatformChatTelemetry(
    Guid? ConversationId,
    Guid? ChatTurnId,
    string InvocationKind,
    int InvocationSequence,
    int MemoryCharacterCount);

internal sealed record PlatformChatChunk(
    string? Text,
    long? InputTokenCount = null,
    long? OutputTokenCount = null,
    string? Role = null,
    IReadOnlyList<PlatformChatContent>? Contents = null,
    IReadOnlyDictionary<string, long>? AdditionalUsageCounts = null);

public sealed class PlatformChatClient : IChatClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IPlatformToolInvoker _tools;
    private readonly AgentLlmSelection _selection;
    private int _invocationSequence;

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
        var messageList = messages.ToList();
        var invocationSequence = Interlocked.Increment(ref _invocationSequence);
        var configuredKind = string.IsNullOrWhiteSpace(_selection.Invocation?.InvocationKind)
            ? "agent-inference"
            : _selection.Invocation.InvocationKind.Trim();
        var invocationKind = configuredKind == "primary" && HasFunctionResult(messageList)
            ? "tool-followup"
            : configuredKind;
        var payload = new PlatformChatRequest(
            _selection.ProviderProfileId,
            _selection.Model,
            messageList.Select(ToBrokerMessage).ToList(),
            options?.Instructions,
            options?.Tools?
                .OfType<AIFunctionDeclaration>()
                .Select(tool => new PlatformChatTool(
                    tool.Name,
                    tool.Description,
                    tool.JsonSchema.Clone()))
                .ToList(),
            new PlatformChatTelemetry(
                _selection.Invocation?.ConversationId,
                _selection.Invocation?.ChatTurnId,
                invocationKind,
                invocationSequence,
                CountMemoryCharacters(messageList)),
            options?.Reasoning?.Output,
            options?.Reasoning?.Effort);
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
                        OutputTokenCount = chunk.OutputTokenCount,
                        AdditionalCounts = chunk.AdditionalUsageCounts is null
                            ? null
                            : new AdditionalPropertiesDictionary<long>(chunk.AdditionalUsageCounts)
                    })
                ]);
            }
        }
    }

    private static bool HasFunctionResult(IEnumerable<ChatMessage> messages) =>
        messages.SelectMany(message => message.Contents).Any(content => content is FunctionResultContent);

    private static int CountMemoryCharacters(IEnumerable<ChatMessage> messages)
    {
        const string startTag = "<memory_context>";
        const string endTag = "</memory_context>";
        var total = 0;
        foreach (var text in messages.Select(message => message.Text).Where(text => !string.IsNullOrEmpty(text)))
        {
            var offset = 0;
            while (offset < text!.Length)
            {
                var start = text.IndexOf(startTag, offset, StringComparison.OrdinalIgnoreCase);
                if (start < 0) break;
                start += startTag.Length;
                var end = text.IndexOf(endTag, start, StringComparison.OrdinalIgnoreCase);
                if (end < 0) break;
                total = checked(total + end - start);
                offset = end + endTag.Length;
            }
        }
        return total;
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
        TextReasoningContent reasoning => new PlatformChatContent(
            "reasoning",
            Text: reasoning.Text,
            ProtectedData: reasoning.ProtectedData),
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
        AgentMediaReferenceContent media => new PlatformChatContent(
            "media_reference",
            AttachmentId: media.AttachmentId,
            MessageId: media.MessageId,
            ConversationId: media.ConversationId,
            FileName: media.FileName,
            ContentType: media.ContentType,
            SizeBytes: media.SizeBytes,
            Sha256: media.Sha256),
        _ => throw new NotSupportedException(
            $"Platform chat messages do not support {content.GetType().Name} content.")
    };

    private static AIContent ToAiContent(PlatformChatContent content) => content.Kind switch
    {
        "text" => new TextContent(content.Text ?? string.Empty),
        "reasoning" => new TextReasoningContent(content.Text ?? string.Empty)
        {
            ProtectedData = content.ProtectedData
        },
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
