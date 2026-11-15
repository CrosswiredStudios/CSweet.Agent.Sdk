namespace CSweet.Agent.SDK;

/// <summary>A planning responsibility, separate from observed execution failure and user approval.</summary>
public sealed record PlanningFinding(string Key, string Statement, string Detail, IReadOnlyList<string> AffectedScopeKeys,
    IReadOnlyList<string> AffectedScopeNames, string AccountableActor, string NextAction, bool UserDecisionRequired = false)
{
    /// <summary>Human-facing status; machine references belong in diagnostics.</summary>
    public string StatusSummary => $"Planning reconciliation: {Statement} Affected work: {string.Join(", ", AffectedScopeNames)}. " +
        $"Accountable: {AccountableActor}. Next: {NextAction}. " + (UserDecisionRequired ? "A user decision is required." : "No user decision is requested.");
}
