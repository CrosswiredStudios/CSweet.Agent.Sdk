using System.Text.Json;

namespace CSweet.Agent.SDK;

/// <summary>Requests one declared connector operation. Authority and account are derived by the host.</summary>
public sealed record RequestConnectorAction(string Capability, JsonElement Input, string IdempotencyKey);
public sealed record ReadConnectorAction(Guid ActionId);

/// <summary>Durable status, not a claim that approval itself executed an external change.</summary>
public sealed record ConnectorAction(Guid ActionId, string Capability, string Status,
    DateTimeOffset UpdatedAt, JsonElement? Result = null, string? ConditionCode = null);

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
        return _platform.InvokeAsync<RequestConnectorAction, ConnectorAction>(PlatformCapabilities.ConnectorActionRequest, request, token);
    }

    public Task<ConnectorAction> ReadActionAsync(ReadConnectorAction request, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.ActionId == Guid.Empty) throw new ArgumentException("An action ID is required.", nameof(request));
        return _platform.InvokeAsync<ReadConnectorAction, ConnectorAction>(PlatformCapabilities.ConnectorActionRead, request, token);
    }
}
