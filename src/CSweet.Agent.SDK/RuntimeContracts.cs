using System.Text.Json;
using CSweet.Agent.Contracts.Packaging;
using Microsoft.Extensions.AI;

namespace CSweet.Agent.SDK;

public enum AgentWorkKind
{
    Capability,
    Event,
    Shutdown
}

public sealed record AgentCapabilityRequest(
    Guid WorkId,
    string Capability,
    JsonElement Arguments,
    string? CorrelationId = null,
    string? SourceAgentId = null)
{
    public JsonElement Payload => Arguments;
    public string RequestingAgentId => SourceAgentId ?? string.Empty;
}

public sealed record AgentEventEnvelope(
    Guid WorkId,
    string EventType,
    JsonElement Data,
    DateTimeOffset OccurredAt,
    string? CorrelationId = null)
{
    public string EventId => WorkId.ToString("N");
    public JsonElement Payload => Data;
}

public sealed record AgentWorkResult(
    bool Succeeded,
    JsonElement? Value = null,
    string? Error = null)
{
    public static AgentWorkResult Success<T>(T value) =>
        new(true, JsonSerializer.SerializeToElement(value));

    public static AgentWorkResult Failure(string error) =>
        new(false, Error: error);
}

public sealed record AgentRuntimeSession(
    string SessionId,
    DateTimeOffset ExpiresAt,
    long GrantRevision,
    AgentIdentity? Identity);

public sealed record AgentWorkLease(
    Guid WorkId,
    int Attempt,
    AgentWorkKind Kind,
    string Name,
    JsonElement Payload,
    string LeaseToken,
    DateTimeOffset LeaseExpiresAt,
    DateTimeOffset Deadline,
    string? CorrelationId);

internal sealed record AgentToolDescriptor(
    string Capability,
    string Name,
    string Description,
    JsonElement InputSchema,
    JsonElement? OutputSchema,
    long GrantRevision,
    bool ModelVisible);

internal interface IPlatformToolInvoker
{
    Task<JsonElement> InvokeAsync(
        string capability,
        JsonElement arguments,
        CancellationToken cancellationToken = default);

    IAsyncEnumerable<JsonElement> InvokeStreamingAsync(
        string capability,
        JsonElement arguments,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AgentToolDescriptor>> ListToolsAsync(
        CancellationToken cancellationToken = default);
}

internal interface IAgentProgressReporter
{
    Task ReportAsync(
        object? value,
        CancellationToken cancellationToken = default);
}

internal interface IAgentRuntimeTransport : IPlatformToolInvoker, IAsyncDisposable
{
    AgentRuntimeSession? Session { get; }

    Task<AgentRuntimeSession> InitializeAsync(
        AgentManifest manifest,
        CancellationToken cancellationToken);

    Task<AgentWorkLease?> ClaimAsync(
        int maximumItems,
        CancellationToken cancellationToken);

    Task RenewWorkAsync(
        AgentWorkLease lease,
        CancellationToken cancellationToken);

    Task ReportProgressAsync(
        AgentWorkLease lease,
        long sequence,
        JsonElement value,
        CancellationToken cancellationToken);

    Task CompleteAsync(
        AgentWorkLease lease,
        AgentWorkResult result,
        CancellationToken cancellationToken);

    Task FailAsync(
        AgentWorkLease lease,
        string error,
        CancellationToken cancellationToken);

    Task CompleteRuntimeAsync(
        AgentWorkResult result,
        CancellationToken cancellationToken);
}
