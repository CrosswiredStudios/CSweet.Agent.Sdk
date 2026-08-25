using System.Text.Json;

namespace CSweet.Agent.SDK;

/// <summary>
/// Read-only helpers for deriving durable coordination progress from persisted turns.
/// Business-specific stage ownership remains in the managing agent.
/// </summary>
public sealed class AgentCoordinationTranscript
{
    private static readonly JsonSerializerOptions DefaultOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IReadOnlyList<AgentCoordinationTurn> _turns;

    public AgentCoordinationTranscript(IReadOnlyList<AgentCoordinationTurn> turns) =>
        _turns = turns ?? throw new ArgumentNullException(nameof(turns));

    public AgentCoordinationTurn? LatestTurn(Guid? speakerOrganizationUserId = null) =>
        _turns.Where(turn => !speakerOrganizationUserId.HasValue ||
                            turn.SpeakerOrganizationUserId == speakerOrganizationUserId.Value)
            .OrderByDescending(turn => turn.Ordinal)
            .FirstOrDefault();

    public AgentCoordinationTurn? LatestArtifactTurn(
        IEnumerable<string> artifactTypes,
        Guid? speakerOrganizationUserId = null)
    {
        var types = artifactTypes.ToHashSet(StringComparer.Ordinal);
        return _turns.Where(turn => turn.Artifact is not null &&
                                    types.Contains(turn.Artifact.Type) &&
                                    (!speakerOrganizationUserId.HasValue ||
                                     turn.SpeakerOrganizationUserId == speakerOrganizationUserId.Value))
            .OrderByDescending(turn => turn.Ordinal)
            .FirstOrDefault();
    }

    public IReadOnlyList<AgentCoordinationTurn> ArtifactTurns(
        IEnumerable<string> artifactTypes,
        Guid? speakerOrganizationUserId = null)
    {
        var types = artifactTypes.ToHashSet(StringComparer.Ordinal);
        return _turns.Where(turn => turn.Artifact is not null &&
                                    types.Contains(turn.Artifact.Type) &&
                                    (!speakerOrganizationUserId.HasValue ||
                                     turn.SpeakerOrganizationUserId == speakerOrganizationUserId.Value))
            .OrderBy(turn => turn.Ordinal)
            .ToArray();
    }

    public T DeserializeArtifact<T>(
        AgentCoordinationTurn turn,
        JsonSerializerOptions? options = null)
    {
        if (turn.Artifact is null)
            throw new InvalidOperationException("The coordination turn does not contain an artifact.");
        return turn.Artifact.Payload.Deserialize<T>(options ?? DefaultOptions)
               ?? throw new JsonException(
                   $"The coordination artifact '{turn.Artifact.Type}' is empty or incompatible with {typeof(T).Name}.");
    }

    public bool HasArtifactDigest(string digest) =>
        !string.IsNullOrWhiteSpace(digest) && _turns.Any(turn =>
            string.Equals(turn.Artifact?.Digest, digest, StringComparison.OrdinalIgnoreCase));
}
