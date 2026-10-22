using System.Text.Json;

namespace CSweet.Agent.SDK;

/// <summary>Operating-state bounds shared by clients, test runtimes, and the platform.</summary>
public static class AgentOperatingStateValidation
{
    public const int MaximumSchemaIdLength = 160;
    public const int MaximumStatusLength = 80;
    public const int MaximumFingerprintLength = 128;
    public const int MaximumIdempotencyKeyLength = 160;
    public const int MaximumCollectionCount = 32;
    public const int MaximumRevisionNameLength = 80;
    public const int MaximumRevisionValueLength = 160;
    public const int MaximumConditionLength = 80;
    public const int MaximumCommitmentLength = 200;
    public const int MaximumPayloadCharacters = 128_000;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static JsonElement ReadInputSchema { get; } = JsonDocument.Parse($$$"""
        {"type":"object","required":["stateKey"],"properties":{"stateKey":{{{KeySchema}}}},"additionalProperties":false}
        """).RootElement.Clone();

    public static JsonElement WriteInputSchema { get; } = JsonDocument.Parse($$$"""
        {"type":"object","required":["stateKey","schemaId","schemaVersion","status","sourceRevisions","conditionCodes","decisionFingerprint","openCommitmentCorrelations","attentionReviewId","payload","idempotencyKey"],"properties":{
        "stateKey":{{{KeySchema}}},
        "schemaId":{"type":"string","minLength":1,"maxLength":{{{MaximumSchemaIdLength}}}},
        "schemaVersion":{"type":"integer","minimum":1},
        "status":{"type":"string","minLength":1,"maxLength":{{{MaximumStatusLength}}}},
        "sourceRevisions":{"type":"object","maxProperties":{{{MaximumCollectionCount}}},"additionalProperties":true},
        "conditionCodes":{"type":"array","maxItems":{{{MaximumCollectionCount}}},"items":{"type":"string","minLength":1,"maxLength":{{{MaximumConditionLength}}}}},
        "decisionFingerprint":{"type":"string","minLength":1,"maxLength":{{{MaximumFingerprintLength}}}},
        "openCommitmentCorrelations":{"type":"array","maxItems":{{{MaximumCollectionCount}}},"items":{"type":"string","minLength":1,"maxLength":{{{MaximumCommitmentLength}}}}},
        "attentionReviewId":{"type":"string","format":"uuid"},"payload":{"type":"object"},
        "expectedRevision":{"type":["integer","null"],"minimum":0},
        "idempotencyKey":{"type":"string","minLength":1,"maxLength":{{{MaximumIdempotencyKeyLength}}}}},"additionalProperties":false}
        """).RootElement.Clone();

    private static string KeySchema => $$$"""
        {"type":"string","minLength":1,"maxLength":{{{AgentOperatingStateKey.MaximumLength}}},"pattern":{{{JsonSerializer.Serialize(AgentOperatingStateKey.Pattern)}}}}
        """;

    public static void Validate(AgentOperatingStateReadRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        AgentOperatingStateKey.Validate(request.StateKey);
    }

    public static void Validate(AgentOperatingStateWriteRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        AgentOperatingStateKey.Validate(request.StateKey);
        Text(request.SchemaId, MaximumSchemaIdLength, nameof(request.SchemaId));
        Text(request.Status, MaximumStatusLength, nameof(request.Status));
        Text(request.DecisionFingerprint, MaximumFingerprintLength, nameof(request.DecisionFingerprint));
        Text(request.IdempotencyKey, MaximumIdempotencyKeyLength, nameof(request.IdempotencyKey));
        if (request.SchemaVersion < 1 || request.ExpectedRevision < 0)
            throw new ArgumentException("Operating-state schema versions must be positive and expected revisions nonnegative.");
        if (request.SourceRevisions is null || request.SourceRevisions.Count > MaximumCollectionCount ||
            request.ConditionCodes is null || request.ConditionCodes.Count > MaximumCollectionCount ||
            request.OpenCommitmentCorrelations is null || request.OpenCommitmentCorrelations.Count > MaximumCollectionCount)
            throw new ArgumentException($"Operating-state collections are required and limited to {MaximumCollectionCount} entries.");
        foreach (var (name, revision) in request.SourceRevisions)
        {
            Text(name, MaximumRevisionNameLength, "Source revision name");
            if (revision is null || revision.Length > MaximumRevisionValueLength)
                throw new ArgumentException("Operating-state source revision values exceed platform bounds.");
        }
        foreach (var condition in request.ConditionCodes) Text(condition, MaximumConditionLength, "Condition code");
        foreach (var commitment in request.OpenCommitmentCorrelations) Text(commitment, MaximumCommitmentLength, "Commitment correlation");
        if (request.Payload.ValueKind != JsonValueKind.Object || request.Payload.GetRawText().Length > MaximumPayloadCharacters)
            throw new ArgumentException($"Operating-state payload must be an object within {MaximumPayloadCharacters} serialized characters.");
    }

    /// <summary>Also validates raw tool calls registered by agent tests.</summary>
    public static void ValidateInput(string capability, JsonElement arguments)
    {
        try
        {
            if (capability is not (PlatformCapabilities.AgentOperatingStateRead or PlatformCapabilities.AgentOperatingStateWrite)) return;
            var schema = capability == PlatformCapabilities.AgentOperatingStateRead ? ReadInputSchema : WriteInputSchema;
            if (arguments.ValueKind != JsonValueKind.Object)
                throw new ArgumentException("Operating-state request must be an object.");
            foreach (var required in schema.GetProperty("required").EnumerateArray())
                if (!arguments.TryGetProperty(required.GetString()!, out _))
                    throw new ArgumentException($"Operating-state request requires {required.GetString()}.");
            foreach (var property in arguments.EnumerateObject())
                if (!schema.GetProperty("properties").TryGetProperty(property.Name, out _))
                    throw new ArgumentException("Operating-state request contains an unsupported field.");
            if (capability == PlatformCapabilities.AgentOperatingStateRead)
                Validate(arguments.Deserialize<AgentOperatingStateReadRequest>(Json)!);
            else if (capability == PlatformCapabilities.AgentOperatingStateWrite)
                Validate(arguments.Deserialize<AgentOperatingStateWriteRequest>(Json)!);
        }
        catch (Exception error) when (error is JsonException or ArgumentException)
        {
            throw new PlatformCapabilityException(capability, PlatformCapabilityErrorCode.ValidationFailed,
                "Invalid operating-state request: " + error.Message, error);
        }
    }

    private static void Text(string? value, int maximum, string field)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > maximum)
            throw new ArgumentException($"Operating-state {field} requires 1-{maximum} nonblank characters.");
    }
}
