using System.Text.Json;
using CSweet.WorkManagement.Contracts;

namespace CSweet.Agent.SDK.Tests;

public sealed class IssueCoordinationTests
{
    [Fact]
    public void ParaphrasedFindingsRetainIdentityAndDistinctMeaningsRemainSeparate()
    {
        var first = PlanningObligationCatalog.Bind([], [new("The review package includes deployment instructions", ["approved"]),
            new("The audio loop is reviewed", ["approved"])], "business-source");
        var next = PlanningObligationCatalog.Bind(first, [new("Provide instructions to run the reviewed build", ["approved"], 0),
            new("Review the generated music loop", ["approved"], 1)], "business-source");
        Assert.Equal(first.Select(x => (x.Key,x.Statement)), next.Select(x => (x.Key,x.Statement))); Assert.NotEqual(next[0].Key, next[1].Key);
        Assert.Throws<ArgumentException>(() => PlanningObligationCatalog.Bind(first, [new("Instructions again", ["approved"])], "business-source"));
        Assert.Throws<ArgumentException>(() => PlanningObligationCatalog.Bind(first, [new("Merged distinct concerns", ["approved"], 0)], "business-source"));
    }

    [Fact]
    public void ChangedApprovedSourcesCanRetireAnObligationButCannotInventReferences()
    {
        var old = new[] { new PlanningObligationIdentity("source-owned", "Deliver a report", ["old-source"]) };
        var changed = PlanningObligationCatalog.Bind(old, [new("Deliver a dashboard", ["new-source"])], "source", true);
        Assert.NotEqual(old[0].Key, changed[0].Key);
        Assert.Throws<ArgumentException>(() => PlanningObligationCatalog.Bind(old, [new("Unknown", ["new-source"], 9)], "source", true));
    }

    [Fact]
    public void SamePatternSupportsNonGameObligationsWithoutEpicsOrNumericRoles()
    {
        var old = PlanningObligationCatalog.Bind([], [new("The hiring manager approves the exact submitted role", ["hiring-policy"])], "organization");
        Assert.Equal(old[0].Key, PlanningObligationCatalog.Bind(old, [new("Review the submitted staffing package", ["hiring-policy"], 0)], "organization")[0].Key);
    }

    public sealed record TestLedger(IReadOnlyList<string> References);

    [Fact]
    public async Task InterruptedAcceptedWriteIsRecoveredAndSameTimestampDifferentContentGetsAnotherMutation()
    {
        AgentOperatingStateResponse? saved = null; var writes = new List<AgentOperatingStateWriteRequest>(); var interrupt = true;
        var runtime = new AgentTestRuntime()
            .RegisterCapability<AgentOperatingStateReadRequest, AgentOperatingStateReadResponse>(PlatformCapabilities.AgentOperatingStateRead,
                (_, _) => Task.FromResult(new AgentOperatingStateReadResponse(saved)))
            .RegisterCapability<AgentOperatingStateWriteRequest, AgentOperatingStateResponse>(PlatformCapabilities.AgentOperatingStateWrite, (r, _) => {
                writes.Add(r);
                saved = new(Guid.NewGuid(), r.StateKey, r.SchemaId, r.SchemaVersion, r.Status, r.SourceRevisions, r.ConditionCodes,
                    r.DecisionFingerprint, r.OpenCommitmentCorrelations, r.AttentionReviewId, r.Payload, (saved?.Revision ?? 0) + 1, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
                if (interrupt) { interrupt = false; throw new IOException("The accepted response was interrupted."); }
                return Task.FromResult(saved);
            });
        var context = runtime.CreateContext();
        await Assert.ThrowsAsync<IOException>(() => DurableAgentState.UpdateAsync<TestLedger>(context.Platform, "ledger", "test.v1", 1, _ => new(["first"])));
        var recovered = await DurableAgentState.UpdateAsync<TestLedger>(context.Platform, "ledger", "test.v1", 1, _ => new(["first"]));
        Assert.Single(writes); Assert.Equal("first", Assert.Single(recovered.Payload.References));
        var changed = await DurableAgentState.UpdateAsync<TestLedger>(context.Platform, "ledger", "test.v1", 1, _ => new(["first", "commission"]));
        Assert.Equal(2, writes.Count); Assert.NotEqual(writes[0].IdempotencyKey, writes[1].IdempotencyKey);
        Assert.Equal(2, changed.Payload.References.Count);
    }
    [Fact]
    public async Task TerminalReceiptRetiresOnlyIdleOwnerTrackingAndNeverTheFailedAttemptOrActualWork()
    {
        var owner = Guid.NewGuid(); var project = Guid.NewGuid(); var session = Guid.NewGuid();
        var tracked = JsonSerializer.Deserialize<PersonalTodoItem>("{}")! with { Id = Guid.NewGuid(), Revision = 2,
            Status = PersonalTodoStatuses.Blocked, CorrelationId = TrackedCollaboration.Correlation(session),
            WorkContext = new(WorkstreamId: project, CoordinationSessionId: session) };
        var running = tracked with { Id = Guid.NewGuid(), Status = PersonalTodoStatuses.Running };
        var actual = tracked with { Id = Guid.NewGuid(), CorrelationId = "actual-deliverable" };
        var archived = new List<Guid>();
        var runtime = new AgentTestRuntime().RegisterCapability<object,PersonalTodoDirectory>(PersonalTodoCapabilities.Read,
            (_,_) => Task.FromResult(new PersonalTodoDirectory([new(Guid.NewGuid(), owner, "Manager", null, null, 1, [tracked,running,actual]),
                new(Guid.NewGuid(), Guid.NewGuid(), "Other owner", null, null, 1, [tracked with { Id = Guid.NewGuid() }]) ])))
            .RegisterCapability<ArchivePersonalTodoItemRequest,PersonalTodoItem>(PersonalTodoCapabilities.Archive,
                (r,_) => { archived.Add(r.ItemId); return Task.FromResult(tracked); });
        var issue = new WorkIssueCaseResponse(Guid.NewGuid(), Guid.NewGuid(), project, "feasibility", "Approved feasibility", "Original failed review retained",
            "Resolved", "AwaitingReview", owner, "Continue accepted work", new(Guid.NewGuid(),Guid.NewGuid(),"source"), [], [new("Coordination", session)], "Verified exact replacement", 3, DateTimeOffset.UtcNow);
        await runtime.CreateContext().Platform.Issues.RetireTrackingAsync(issue, owner);
        Assert.Equal(new[] { tracked.Id }, archived);
        await Assert.ThrowsAsync<ArgumentException>(() => runtime.CreateContext().Platform.Issues.RetireTrackingAsync(issue with { Status = "Open" }, owner));
    }

}
