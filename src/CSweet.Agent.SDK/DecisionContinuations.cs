using CSweet.WorkManagement.Contracts;

namespace CSweet.Agent.SDK;

/// <summary>Follows explicit decision replacements, never prose or a fixed replacement depth.</summary>
public static class DecisionContinuations
{
    public static DecisionRecord? Current(Guid id, Guid workstream, IReadOnlyList<DecisionRecord> decisions)
    {
        var visited = new HashSet<Guid>();
        while (visited.Add(id))
        {
            var value = decisions.SingleOrDefault(d => d.Id == id && d.WorkstreamId == workstream);
            if (value is null) return null;
            if (value.SupersededByDecisionId is not Guid next) return value.Status == DecisionStatuses.Superseded ? null : value;
            id = next;
        }
        return null;
    }

    public static async Task<DecisionRecord?> ReadCurrentAsync(PlatformCapabilityClient platform, Guid id, Guid workstream,
        CancellationToken ct = default)
    {
        var visited = new HashSet<Guid>();
        while (visited.Add(id))
        {
            var value = (await platform.ReadDecisionsAsync(new(id), ct)).SingleOrDefault(d => d.Id == id && d.WorkstreamId == workstream);
            if (value is null) return null;
            if (value.SupersededByDecisionId is not Guid next) return value.Status == DecisionStatuses.Superseded ? null : value;
            id = next;
        }
        return null;
    }
}
