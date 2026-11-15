namespace CSweet.Agent.SDK.Tests;

public sealed class PlanningScopeOwnershipTests
{
    [Fact]
    public void HoldsFollowContainmentAndDependencyConsumersWithoutHoldingIndependentWorkOrPrerequisites()
    {
        PlanningScopeDependency[] scopes = [new("foundation", []), new("visuals", ["foundation"]),
            new("polish", [], "visuals"), new("release", ["polish"]), new("controls", ["foundation"])];
        var held = PlanningScopeOwnership.BlockedScopes(scopes, ["visuals"]);
        Assert.Equal(new[] { "polish", "release", "visuals" }, held.Order().ToArray());
        Assert.DoesNotContain("controls", held); Assert.DoesNotContain("foundation", held);
        Assert.Empty(PlanningScopeOwnership.BlockedScopes(scopes, []));
        Assert.Throws<ArgumentException>(() => PlanningScopeOwnership.BlockedScopes(scopes, ["foreign-scope"]));
    }

    [Fact]
    public void BindingsRetainExactCodeOwnedConstraintsAndDecisionsAndRejectInventedReferences()
    {
        PlanningOwnershipEvidence[] catalog = [new("constraint:1", "Constraint", "Fixed camera", ["core", "visuals"]),
            new("decision:2", "Decision", "Use procedural assets", ["visuals"])];
        var bound = PlanningScopeOwnership.BindEvidence([1, 0, 1], catalog);
        Assert.Equal(2, bound.Count); Assert.Same(catalog[1], bound[0]); Assert.Same(catalog[0], bound[1]);
        Assert.Throws<ArgumentException>(() => PlanningScopeOwnership.BindEvidence([2], catalog));
        Assert.Throws<ArgumentException>(() => PlanningScopeOwnership.BindEvidence([], catalog));
    }

    [Fact]
    public void ReconciliationStatusNamesImpactActorAndActionWithoutClaimingAnObservedFailureOrUserApproval()
    {
        var finding = new PlanningFinding("internal-key", "Assign the conditional FPS escalation responsibility.",
            "No actual observation failure is recorded.", ["internal-epic-key"], ["Performance evidence"], "Technical lead",
            "Confirm the existing constraint or propose its missing owner");
        Assert.Contains("Technical lead", finding.StatusSummary); Assert.Contains("Performance evidence", finding.StatusSummary);
        Assert.Contains("No user decision is requested", finding.StatusSummary);
        Assert.DoesNotContain("internal-epic-key", finding.StatusSummary); Assert.False(finding.UserDecisionRequired);
        Assert.DoesNotContain("measurement failed", finding.StatusSummary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PersonalResultTextRetainsLongWaitingBlockingAndCompletionDetails()
    {
        var text = string.Concat(Enumerable.Repeat("Finding detail 🎮\n", 20_000));
        Assert.Equal(text.Trim(), PersonalTodoResult.WaitingUntil(DateTimeOffset.UtcNow.AddMinutes(5), text).Content);
        Assert.Equal(text.Trim(), PersonalTodoResult.Blocked(text).Content);
        Assert.Equal(text.Trim(), PersonalTodoResult.Completed(text).Content);
    }
}
