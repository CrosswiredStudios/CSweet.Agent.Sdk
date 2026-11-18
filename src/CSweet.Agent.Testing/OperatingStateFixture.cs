using System.Text.Json;
using CSweet.Agent.SDK;

namespace CSweet.Agent.Testing;

/// <summary>Registers in-memory operating-state reads and writes for one agent installation.</summary>
public sealed class OperatingStateFixture
{
    private readonly Dictionary<string, AgentOperatingStateResponse> _states = new(StringComparer.Ordinal);
    private readonly Dictionary<(string StateKey, string MutationKey), Receipt> _receipts = [];
    private readonly object _gate = new();
    private readonly TimeProvider _clock;

    public OperatingStateFixture(AgentTestRuntime runtime, TimeProvider? clock = null)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        _clock = clock ?? TimeProvider.System;
        runtime.RegisterCapability<AgentOperatingStateReadRequest, AgentOperatingStateReadResponse>(
            PlatformCapabilities.AgentOperatingStateRead, (request, token) =>
            {
                token.ThrowIfCancellationRequested();
                lock (_gate)
                    return Task.FromResult(new AgentOperatingStateReadResponse(_states.GetValueOrDefault(request.StateKey)));
            });
        runtime.RegisterCapability<AgentOperatingStateWriteRequest, AgentOperatingStateResponse>(
            PlatformCapabilities.AgentOperatingStateWrite, (request, token) =>
            {
                token.ThrowIfCancellationRequested();
                lock (_gate)
                    return Task.FromResult(Write(request));
            });
    }

    /// <summary>Seeds a new key with a typed object and synthetic fixture metadata.</summary>
    public void Seed<T>(string stateKey, T payload, long revision = 1)
    {
        var now = _clock.GetUtcNow();
        Seed(new AgentOperatingStateResponse(Guid.NewGuid(), stateKey, "fixture", 1, "Active",
            new Dictionary<string, string>(), [], "fixture", [], Guid.NewGuid(),
            JsonSerializer.SerializeToElement(payload, JsonSerializerOptions.Web), revision, now, now));
    }

    /// <summary>Seeds an exact checkpoint envelope. Mutation receipts start with the first write through the fixture.</summary>
    public void Seed(AgentOperatingStateResponse checkpoint)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        ArgumentOutOfRangeException.ThrowIfLessThan(checkpoint.Revision, 1);
        AgentOperatingStateValidation.Validate(new AgentOperatingStateWriteRequest(checkpoint.StateKey,
            checkpoint.SchemaId, checkpoint.SchemaVersion, checkpoint.Status, checkpoint.SourceRevisions,
            checkpoint.ConditionCodes, checkpoint.DecisionFingerprint, checkpoint.OpenCommitmentCorrelations,
            checkpoint.AttentionReviewId, checkpoint.Payload, null, "fixture-seed"));
        var snapshot = checkpoint with
        {
            Payload = checkpoint.Payload.Clone(),
            SourceRevisions = new Dictionary<string, string>(checkpoint.SourceRevisions),
            ConditionCodes = checkpoint.ConditionCodes.ToArray(),
            OpenCommitmentCorrelations = checkpoint.OpenCommitmentCorrelations.ToArray()
        };
        lock (_gate)
            _states.Add(snapshot.StateKey, snapshot);
    }

    private AgentOperatingStateResponse Write(AgentOperatingStateWriteRequest request)
    {
        // Mirrors the broker's durable mutation identity. A retry from a later review can
        // carry a different attention review ID or expected revision and still replay its receipt.
        var content = InfrastructureStateSerializer.Export(new
        {
            request.SchemaId, request.SchemaVersion, request.Status, request.SourceRevisions,
            request.ConditionCodes, request.DecisionFingerprint, request.OpenCommitmentCorrelations, request.Payload
        }).ContentHash;
        var key = (request.StateKey, request.IdempotencyKey);
        if (_receipts.TryGetValue(key, out var receipt))
        {
            if (receipt.ContentHash != content)
                throw Conflict("idempotency.content_mismatch: This mutation identity already binds different state content.");
            return receipt.Result;
        }

        var previous = _states.GetValueOrDefault(request.StateKey);
        if (previous is null && request.ExpectedRevision is not null and not 0)
            throw Conflict("The operating-state record does not exist at the expected revision.");
        if (previous is not null && request.ExpectedRevision != previous.Revision)
            throw Conflict($"Expected operating-state revision {request.ExpectedRevision?.ToString() ?? "none"}; current revision is {previous.Revision}.");

        var now = _clock.GetUtcNow();
        var next = new AgentOperatingStateResponse(previous?.Id ?? Guid.NewGuid(), request.StateKey,
            request.SchemaId, request.SchemaVersion, request.Status, request.SourceRevisions, request.ConditionCodes,
            request.DecisionFingerprint, request.OpenCommitmentCorrelations, request.AttentionReviewId, request.Payload,
            (previous?.Revision ?? 0) + 1, previous?.CreatedAt ?? now, now);
        _states[request.StateKey] = next;
        _receipts.Add(key, new Receipt(content, next));
        return next;
    }

    private static PlatformCapabilityException Conflict(string message) =>
        new(PlatformCapabilities.AgentOperatingStateWrite, PlatformCapabilityErrorCode.Conflict, message,
            failureCode: nameof(PlatformCapabilityErrorCode.Conflict));

    private sealed record Receipt(string ContentHash, AgentOperatingStateResponse Result);
}
