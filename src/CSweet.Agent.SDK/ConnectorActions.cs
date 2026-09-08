using System.Text.Json;
using System.Text.Json.Serialization;

namespace CSweet.Agent.SDK;

/// <summary>Requests one declared connector operation. Authority and account are derived by the host.</summary>
public sealed record RequestConnectorAction(string Capability, JsonElement Input, string IdempotencyKey)
{
    /// <summary>Required for media actions. The host independently checks this retained source before every transfer.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ConversationAttachmentReference? MediaSource { get; init; }
}

/// <summary>A retained conversation attachment, not an access token or permission to publish.</summary>
public sealed record ConversationAttachmentReference(Guid ConversationId, Guid MessageId, Guid AttachmentId);
public sealed record ReadConnectorAction(Guid ActionId);
public sealed record CancelConnectorAction(Guid ActionId, string IdempotencyKey);

/// <summary>Authoritative decision feedback, not a new approval or permission to change the reviewed payload.</summary>
public sealed record ConnectorActionDecision(string Decision, string? Comment, DateTimeOffset DecidedAt);

/// <summary>Durable status, not a claim that approval itself executed an external change.</summary>
public sealed record ConnectorAction(Guid ActionId, string Capability, string Status,
    DateTimeOffset UpdatedAt, JsonElement? Result = null, string? ConditionCode = null,
    ConnectorActionDecision? Decision = null);

public static class ConnectorActionEvents
{
    public const string Changed = "com.csweet.connector.action.changed.v1";
}

/// <summary>A wake hint for the exact requesting installation. Read current status before advancing work.</summary>
public sealed record ConnectorActionChanged(Guid ActionId, string Capability, string Status);

/// <summary>Provider-neutral, grant-governed access to durable connector actions.</summary>
public sealed class PlatformConnectorClient
{
    private readonly PlatformCapabilityClient _platform;
    internal PlatformConnectorClient(PlatformCapabilityClient platform) => _platform = platform;

    public Task<ConnectorAction> RequestActionAsync(RequestConnectorAction request, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Capability) || request.Capability.Length > 200 ||
            request.Input.ValueKind != JsonValueKind.Object || string.IsNullOrWhiteSpace(request.IdempotencyKey) ||
            request.IdempotencyKey.Length > 160 || request.IdempotencyKey.Any(char.IsControl))
            throw new ArgumentException("A declared operation, object input and stable bounded idempotency key are required.", nameof(request));
        if (request.MediaSource is { } source && (source.ConversationId == Guid.Empty ||
            source.MessageId == Guid.Empty || source.AttachmentId == Guid.Empty))
            throw new ArgumentException("A media source requires the exact conversation, message and attachment.", nameof(request));
        return _platform.InvokeAsync<RequestConnectorAction, ConnectorAction>(PlatformCapabilities.ConnectorActionRequest, request, token);
    }

    public Task<ConnectorAction> ReadActionAsync(ReadConnectorAction request, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.ActionId == Guid.Empty) throw new ArgumentException("An action ID is required.", nameof(request));
        return _platform.InvokeAsync<ReadConnectorAction, ConnectorAction>(PlatformCapabilities.ConnectorActionRead, request, token);
    }

    /// <summary>Cancel this installation's action only before execution starts. Unknown outcomes cannot be cancelled away.</summary>
    public Task<ConnectorAction> CancelActionAsync(CancelConnectorAction request, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.ActionId == Guid.Empty || string.IsNullOrWhiteSpace(request.IdempotencyKey) ||
            request.IdempotencyKey.Length > 160 || request.IdempotencyKey.Any(char.IsControl))
            throw new ArgumentException("An action ID and stable bounded cancellation key are required.", nameof(request));
        return _platform.InvokeAsync<CancelConnectorAction, ConnectorAction>(PlatformCapabilities.ConnectorActionCancel, request, token);
    }
}
