namespace CSweet.Agent.SDK;

public sealed record PlanningObligationIdentity(string Key, string Statement, IReadOnlyList<string> Sources)
{
    public bool IsCurrent { get; init; } = true;
}
public sealed record PlanningObligationJudgment(string Statement, IReadOnlyList<string> Sources, int? ExistingIndex = null);

/// <summary>Models judge equivalence; code preserves identity and validates selections. No text-similarity approval.</summary>
public static class PlanningObligationCatalog
{
    public static IReadOnlyList<PlanningObligationIdentity> Bind(IReadOnlyList<PlanningObligationIdentity> existing,
        IReadOnlyList<PlanningObligationJudgment> judgments, string identityNamespace, bool changedApprovedBasis = false)
    {
        var selected = judgments.Where(j => j.ExistingIndex.HasValue).Select(j => j.ExistingIndex!.Value).ToArray();
        if (selected.Any(i => i < 0 || i >= existing.Count) || selected.Distinct().Count() != selected.Length)
            throw new ArgumentException("Select each retained obligation at most once using its supplied reference.");
        if (!changedApprovedBasis && !Enumerable.Range(0, existing.Count).Where(i => existing[i].IsCurrent).All(selected.Contains))
            throw new ArgumentException("An unchanged accepted basis must account for every retained obligation; rewording cannot replace or discard one.");
        var next = existing.Count;
        return judgments.Select(j => {
            if (string.IsNullOrWhiteSpace(j.Statement) || j.Sources.Count == 0) throw new ArgumentException("An obligation requires grounded meaning and approved sources.");
            return j.ExistingIndex is int index ? existing[index] with { Sources = j.Sources, IsCurrent = true } :
                new PlanningObligationIdentity(AgentOperatingStateKey.Create("obligation:v1", new { identityNamespace, ordinal = next++ }), j.Statement, j.Sources);
        }).ToArray();
    }
}
