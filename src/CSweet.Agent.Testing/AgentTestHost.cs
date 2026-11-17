using System.Text.Json;
using CSweet.Agent.SDK;
using Microsoft.Extensions.Logging.Abstractions;

namespace CSweet.Agent.Testing;

/// <summary>Runs deliveries on one agent instance using the production SDK dispatcher and a scripted platform.</summary>
public sealed class AgentTestHost(
    ICSweetAgent agent, AgentTestRuntime platform, AgentIdentity? identity = null,
    string businessId = "test-organization", string installationId = "00000000-0000-0000-0000-000000000001")
{
    private readonly AgentDeliveryDispatcher _dispatcher = new(agent, NullLogger.Instance);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Reuse the event ID on redelivery. Each delivery gets its own work identity and callback context.</summary>
    public Task<AgentWorkResult> DeliverEventAsync(string eventType, object data, Guid? eventId = null,
        string? correlationId = null, CancellationToken cancellationToken = default)
    {
        if (eventId == Guid.Empty) throw new ArgumentException("Event ID must not be empty.", nameof(eventId));
        return DispatchAsync(AgentWorkKind.Event, eventType, data, eventId ?? Guid.NewGuid(), correlationId, cancellationToken);
    }

    /// <summary>Includes SDK answer validation and submission through the registered coordination capability.</summary>
    public Task<AgentWorkResult> DeliverCoordinationTurnAsync(AgentCoordinationTurnRequest request,
        Guid? eventId = null, CancellationToken cancellationToken = default) =>
        DeliverEventAsync(AgentCoordinationEvents.TurnRequested, request, eventId, cancellationToken: cancellationToken);

    public Task<AgentWorkResult> ExecuteCapabilityAsync(string capability, object arguments,
        string? correlationId = null, CancellationToken cancellationToken = default) =>
        DispatchAsync(AgentWorkKind.Capability, capability, arguments, null, correlationId, cancellationToken);

    public Task<AgentWorkResult> ApplyConfigurationAsync(AgentConfigurationUpdate update, CancellationToken cancellationToken = default) =>
        DispatchAsync(AgentWorkKind.ConfigurationUpdate, "configuration", update, null, null, cancellationToken);

    private async Task<AgentWorkResult> DispatchAsync(AgentWorkKind kind, string name, object data,
        Guid? eventId, string? correlationId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string? missing = null;
        var context = platform.CreateContext(businessId, installationId, identity,
            capability => Interlocked.CompareExchange(ref missing, capability, null));
        var delivery = new AgentDelivery(kind, Guid.NewGuid(), name,
            JsonSerializer.SerializeToElement(data, JsonOptions), eventId, correlationId);
        var result = await _dispatcher.DispatchAsync(delivery, context, cancellationToken);
        if (missing is not null)
            throw new InvalidOperationException($"Delivery '{name}' called unregistered fixture capability '{missing}'.");
        return result.Work;
    }
}
