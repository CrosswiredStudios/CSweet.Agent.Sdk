namespace CSweet.Agent.SDK;

/// <summary>Exact, code-supplied evidence that can own a planning obligation.</summary>
public sealed record PlanningOwnershipEvidence(string Key, string Kind, string Text, IReadOnlyList<string> ScopeKeys);

/// <summary>A planning scope and the scopes it needs, including optional containment.</summary>
public sealed record PlanningScopeDependency(string Key, IReadOnlyList<string> Dependencies, string? ParentKey = null);

/// <summary>
/// Binds judgment to supplied evidence and propagates scoped holds. Callers decide semantic
/// adequacy; these mechanics neither approve a plan nor infer authority from model-written keys.
/// </summary>
public static class PlanningScopeOwnership
{
    public static IReadOnlyList<PlanningOwnershipEvidence> BindEvidence(IReadOnlyList<int> indexes,
        IReadOnlyList<PlanningOwnershipEvidence> catalog)
    {
        if (indexes.Count == 0 || indexes.Any(i => i < 0 || i >= catalog.Count))
            throw new ArgumentException("Select existing ownership evidence from the supplied catalog.");
        return indexes.Distinct().Select(i => catalog[i]).DistinctBy(e => e.Key).ToArray();
    }

    public static IReadOnlySet<string> BlockedScopes(IReadOnlyList<PlanningScopeDependency> scopes,
        IEnumerable<string> affectedScopeKeys)
    {
        var known = scopes.Select(s => s.Key).ToHashSet(StringComparer.Ordinal);
        if (known.Count != scopes.Count || known.Any(string.IsNullOrWhiteSpace) ||
            scopes.Any(s => s.Dependencies.Any(d => !known.Contains(d)) || s.ParentKey is not null && !known.Contains(s.ParentKey)))
            throw new ArgumentException("Scope dependencies must refer to distinct supplied scopes.");
        var blocked = affectedScopeKeys.ToHashSet(StringComparer.Ordinal);
        if (!blocked.IsSubsetOf(known)) throw new ArgumentException("A hold must refer to supplied scope.");
        bool changed;
        do
        {
            changed = false;
            foreach (var scope in scopes)
                if (scope.Dependencies.Any(blocked.Contains) || scope.ParentKey is not null && blocked.Contains(scope.ParentKey))
                    changed |= blocked.Add(scope.Key);
        } while (changed);
        return blocked;
    }
}
