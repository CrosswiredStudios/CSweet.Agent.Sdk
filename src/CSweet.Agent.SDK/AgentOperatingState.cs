using System.Text.Json;

namespace CSweet.Agent.SDK;

public sealed record AgentOperatingStateReadRequest(string StateKey);

public sealed record AgentOperatingStateReadResponse(AgentOperatingStateResponse? State);

public sealed record AgentOperatingStateWriteRequest(
    string StateKey,
    string SchemaId,
    int SchemaVersion,
    string Status,
    IReadOnlyDictionary<string, string> SourceRevisions,
    IReadOnlyList<string> ConditionCodes,
    string DecisionFingerprint,
    IReadOnlyList<string> OpenCommitmentCorrelations,
    Guid AttentionReviewId,
    JsonElement Payload,
    long? ExpectedRevision,
    string IdempotencyKey);

public sealed record AgentOperatingStateResponse(
    Guid Id,
    string StateKey,
    string SchemaId,
    int SchemaVersion,
    string Status,
    IReadOnlyDictionary<string, string> SourceRevisions,
    IReadOnlyList<string> ConditionCodes,
    string DecisionFingerprint,
    IReadOnlyList<string> OpenCommitmentCorrelations,
    Guid AttentionReviewId,
    JsonElement Payload,
    long Revision,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
