using System.Text.Json;
using CSweet.Agent.Contracts.Packaging;
using Microsoft.Extensions.AI;

namespace CSweet.Agent.SDK;

public enum AgentWorkKind
{
    Capability,
    Event,
    ConfigurationUpdate,
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
    Guid EventId,
    string EventType,
    JsonElement Data,
    DateTimeOffset OccurredAt,
    string? CorrelationId = null)
{
    public JsonElement Payload => Data;
}

public sealed record AgentWorkResult(
    bool Succeeded,
    JsonElement? Value = null,
    string? Error = null,
    string? FailureCode = null,
    bool? Retryable = null)
{
    private static readonly JsonSerializerOptions SerializerOptions =
        new(JsonSerializerDefaults.Web);

    /// <summary>Creates a successful result serialized with the SDK's camel-case JSON contract.</summary>
    public static AgentWorkResult Success<T>(T value) =>
        new(true, JsonSerializer.SerializeToElement(value, SerializerOptions));

    /// <summary>Creates a safe expected failure result.</summary>
    public static AgentWorkResult Failure(
        string error,
        string failureCode = "agent.rejected",
        bool retryable = false) =>
        new(false, Error: error, FailureCode: failureCode, Retryable: retryable);
}

public sealed record AgentRuntimeSession(
    string SessionId,
    DateTimeOffset ExpiresAt,
    long GrantRevision,
    AgentIdentity? Identity,
    AgentRuntimeConfiguration? Configuration);

/// <summary>
/// The platform-owned installation configuration captured when a runtime session is established.
/// The SDK applies this snapshot before activation callbacks or durable work are allowed to run.
/// </summary>
public sealed record AgentRuntimeConfiguration(
    string SchemaVersion,
    IReadOnlyDictionary<string, JsonElement> Settings,
    Guid InstallationId = default,
    long DesiredRevision = 0,
    string EffectiveDigest = "");

/// <summary>A durable platform-owned effective-configuration refresh.</summary>
public sealed record AgentConfigurationUpdate(
    Guid InstallationId,
    string SchemaVersion,
    IReadOnlyDictionary<string, JsonElement> EffectiveSettings,
    IReadOnlyList<string> ChangedKeys,
    long DesiredRevision,
    string EffectiveDigest);

/// <summary>The action requested after an agent observes a new settings snapshot.</summary>
public enum ConfigurationApplyResult
{
    Applied,
    RestartRequired
}

/// <summary>Strongly typed context passed to the configuration-change callback.</summary>
public sealed record AgentConfigurationChangedContext(
    AgentSettings Previous,
    AgentSettings Current,
    IReadOnlyList<string> ChangedKeys,
    long DesiredRevision,
    string EffectiveDigest);

public sealed record AgentWorkLease(
    Guid WorkId,
    int Attempt,
    AgentWorkKind Kind,
    string Name,
    JsonElement Payload,
    string LeaseToken,
    DateTimeOffset LeaseExpiresAt,
    DateTimeOffset Deadline,
    Guid? EventId,
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
