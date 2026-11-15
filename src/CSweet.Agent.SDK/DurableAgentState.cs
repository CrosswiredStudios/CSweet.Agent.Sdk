using System.Text.Json;

namespace CSweet.Agent.SDK;

/// <summary>Content-bound compare-and-swap updates. A changed merge can never replay an earlier write's identity.</summary>
public static class DurableAgentState
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public static Task<AgentOperatingState<T>> UpdateAsync<T>(PlatformCapabilityClient platform, string key, string schema,
        int version, Func<T?, T> merge, CancellationToken ct = default, IReadOnlyDictionary<string,string>? sourceRevisions = null, string status = "Active") where T : class
        => UpdateVersionedAsync<T>(platform, key, schema, version, prior => merge(prior is null ? null : prior.Payload), ct, sourceRevisions, status);

    /// <summary>Use when domain merges need the observed revision, for example to distinguish intentional clears from stale snapshots.</summary>
    public static async Task<AgentOperatingState<T>> UpdateVersionedAsync<T>(PlatformCapabilityClient platform, string key, string schema,
        int version, Func<AgentOperatingState<T>?, T> merge, CancellationToken ct = default,
        IReadOnlyDictionary<string,string>? sourceRevisions = null, string status = "Active") where T : class
    {
        for (int attempt = 0; ; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            var prior = await platform.ReadOperatingStateAsync<T>(key, ct);
            var value = merge(prior);
            var payload = JsonSerializer.SerializeToElement(value, Json);
            var revisions = sourceRevisions ?? new Dictionary<string,string>();
            if (prior is not null && prior.Status == status && JsonElement.DeepEquals(JsonSerializer.SerializeToElement(prior.Payload, Json), payload) &&
                JsonElement.DeepEquals(JsonSerializer.SerializeToElement(prior.SourceRevisions), JsonSerializer.SerializeToElement(revisions))) return prior;
            var mutation = AgentOperatingStateKey.Create("state-mutation:v1", new { key, schema, version, expected = prior?.Revision, status, revisions, payload });
            try
            {
                var saved = await platform.WriteOperatingStateAsync(new AgentOperatingStateWriteRequest(key, schema, version, status,
                    revisions, [], mutation, [], Guid.Empty, payload, prior?.Revision, mutation), ct);
                if (!JsonElement.DeepEquals(saved.Payload, payload)) throw new InvalidOperationException("The accepted state receipt does not contain the intended mutation.");
                return new(saved.Id, saved.StateKey, saved.SchemaId, saved.SchemaVersion, saved.Status, saved.SourceRevisions,
                    saved.ConditionCodes, saved.DecisionFingerprint, saved.OpenCommitmentCorrelations, saved.AttentionReviewId,
                    saved.Payload.Deserialize<T>(Json)!, saved.Revision, saved.CreatedAt, saved.UpdatedAt);
            }
            catch (PlatformCapabilityException ex) when (ex.Code == PlatformCapabilityErrorCode.Conflict && attempt < 3 &&
                !ex.Message.Contains("idempotency.content_mismatch", StringComparison.Ordinal)) { }
        }
    }
}
