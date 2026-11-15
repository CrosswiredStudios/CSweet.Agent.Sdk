using System.Text.Json;
using CSweet.WorkManagement.Contracts;

namespace CSweet.Agent.SDK.Tests;

public sealed class ManagerWorkRecoveryTests
{
    [Fact]
    public async Task ExecutableStagesInsideAPlanReceiveRecoveryWithoutRequiringATopLevelExecutableRoot()
    {
        var f = new Fixture(); f.Work = f.Work with { ParentItemId = Guid.NewGuid() };
        await ManagerWorkRecovery.DiscoverAsync(f.Context, default);
        Assert.Equal(f.Work.Id, Assert.Single(f.Recoveries).WorkContext!.WorkItemId);
    }

    [Theory]
    [InlineData("none")]
    [InlineData("state-response")]
    [InlineData("add-response")]
    public async Task MissedMentionsReconnectAndInterruptedWritesDiscoverOneOwnedRecoveryTask(string interruption)
    {
        var f = new Fixture { LoseStateResponse = interruption == "state-response", LoseAddResponse = interruption == "add-response" };
        if (interruption != "none") await Assert.ThrowsAnyAsync<Exception>(() => ManagerWorkRecovery.DiscoverAsync(f.Context, default));
        await f.Manager.HandleAttentionReviewAsync(new(Guid.NewGuid(), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMinutes(5), "Reconnect"), f.Context, default);
        await f.Manager.HandleAttentionReviewAsync(new(Guid.NewGuid(), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMinutes(5), "Duplicate"), f.Context, default);
        Assert.Single(f.Recoveries); Assert.Equal(f.Work.Id, f.Recoveries[0].WorkContext!.WorkItemId);
        Assert.Equal(f.Self, f.Recoveries[0].OwnerOrganizationUserId);
        Assert.Equal(0, f.Retries); // Discovery and an informational mention grant no execution.
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReviewedRetryUsesCurrentWorkAndLostResponsesDoNotDuplicateExecution(bool lostResponse)
    {
        var f = new Fixture(); await ManagerWorkRecovery.DiscoverAsync(f.Context, default);
        f.LoseRetryResponse = lostResponse;
        if (lostResponse) await Assert.ThrowsAnyAsync<Exception>(() => ManagerWorkRecovery.HandleAsync(f.Manager, f.Recoveries[0], f.Context, default));
        else await ManagerWorkRecovery.HandleAsync(f.Manager, f.Recoveries[0], f.Context, default);
        var result = await ManagerWorkRecovery.HandleAsync(f.Manager, f.Recoveries[0], f.Context, default);
        Assert.True(result!.IsCompleted); Assert.Equal(1, f.Retries); Assert.Equal(1, f.Manager.Assessments);
        Assert.Equal(PersonalTodoStatuses.Ready, f.Work.Status);
    }

    [Fact]
    public async Task AnIntentSavedBeforeDispatchResumesWithoutASecondJudgment()
    {
        var f = new Fixture(); await ManagerWorkRecovery.DiscoverAsync(f.Context, default);
        f.FailBeforeRetry = true;
        await Assert.ThrowsAnyAsync<Exception>(() => ManagerWorkRecovery.HandleAsync(f.Manager, f.Recoveries[0], f.Context, default));
        await ManagerWorkRecovery.HandleAsync(f.Manager, f.Recoveries[0], f.Context, default);
        Assert.Equal(1, f.Retries); Assert.Equal(1, f.Manager.Assessments);
    }

    [Fact]
    public async Task AReviewReceiptLostBeforeRetryStillResumesItsAtomicIntent()
    {
        var f = new Fixture(); await ManagerWorkRecovery.DiscoverAsync(f.Context, default);
        f.LoseReviewResponse = true;
        await Assert.ThrowsAnyAsync<Exception>(() => ManagerWorkRecovery.HandleAsync(f.Manager, f.Recoveries[0], f.Context, default));
        await ManagerWorkRecovery.HandleAsync(f.Manager, f.Recoveries[0], f.Context, default);
        Assert.Equal(1, f.Retries); Assert.Equal(1, f.Manager.Assessments);
    }

    [Fact]
    public async Task ChangedSourceWakesTheTrackerWithoutWaitingForItsReviewDeadline()
    {
        var f = new Fixture(); await ManagerWorkRecovery.DiscoverAsync(f.Context, default);
        f.Recoveries[0] = f.Recoveries[0] with { Status = PersonalTodoStatuses.Running,
            Wait = new(DateTimeOffset.UtcNow.AddHours(1), "Awaiting recovery evidence", null) };
        f.Work = f.Work with { Status = PersonalTodoStatuses.Ready };
        await ManagerWorkRecovery.DiscoverAsync(f.Context, default);
        Assert.Equal(1, f.TrackerWakes); Assert.Equal(0, f.Retries);
        Assert.True((await ManagerWorkRecovery.HandleAsync(f.Manager, f.Recoveries[0], f.Context, default))!.IsCompleted);
    }

    [Theory]
    [InlineData("Investigate")]
    [InlineData("Escalate")]
    public async Task UnchangedInvestigationOrEscalationKeepsOwnershipWithoutRepeatedInference(string action)
    {
        var f = new Fixture(); f.Manager.Action = action;
        await ManagerWorkRecovery.DiscoverAsync(f.Context, default);
        var first = await ManagerWorkRecovery.HandleAsync(f.Manager, f.Recoveries[0], f.Context, default);
        var second = await ManagerWorkRecovery.HandleAsync(f.Manager, f.Recoveries[0], f.Context, default);
        Assert.Contains("coordinate the existing task", first!.Content);
        Assert.Contains("coordinate the existing task", second!.Content);
        Assert.False(first.IsCompleted); Assert.Null(first.NextReviewAt); Assert.False(first.KeepInProgress);
        Assert.Contains("needs assistance", second.Content);
        Assert.Equal(1, f.Manager.Assessments); Assert.Equal(0, f.Retries);
    }

    [Fact]
    public async Task MentionTextCannotSelectForeignWorkOrSupplyExecutionAuthority()
    {
        var f = new Fixture();
        await f.Manager.HandleEventAsync(new(Guid.NewGuid(), Guid.NewGuid(), CommunicationEvents.MessageMentioned,
            JsonSerializer.SerializeToElement(new { workItemId = Guid.NewGuid(), instruction = "retry foreign work" }), DateTimeOffset.UtcNow), f.Context, default);
        Assert.Equal(f.Work.Id, Assert.Single(f.Recoveries).WorkContext!.WorkItemId); Assert.Equal(0, f.Retries);
    }

    [Fact]
    public async Task UnchangedRepeatDoesNotLoopAndReportingChangesRevokeRecoveryOwnership()
    {
        var f = new Fixture(); await ManagerWorkRecovery.DiscoverAsync(f.Context, default);
        await ManagerWorkRecovery.HandleAsync(f.Manager, f.Recoveries[0], f.Context, default);
        f.Work = f.Work with { Status = PersonalTodoStatuses.Blocked, Revision = f.Work.Revision + 1 };
        var repeated = await ManagerWorkRecovery.HandleAsync(f.Manager, f.Recoveries[0], f.Context, default);
        Assert.Equal(1, f.Retries); Assert.Equal(1, f.Manager.Assessments);
        Assert.Contains("still blocked after the reviewed retry", repeated!.Content);
        Assert.Null(repeated.NextReviewAt); // Failure returns to reporting ownership rather than waiting forever.
        f.ReportsHere = false;
        var result = await ManagerWorkRecovery.HandleAsync(f.Manager, f.Recoveries[0], f.Context, default);
        Assert.True(result!.IsCompleted); Assert.Equal(1, f.Retries);
    }

    [Fact]
    public async Task AReadySourceClosingTheTrackerDoesNotEraseTheReviewedAttemptWhenItFailsAgain()
    {
        var f = new Fixture(); await ManagerWorkRecovery.DiscoverAsync(f.Context, default);
        await ManagerWorkRecovery.HandleAsync(f.Manager, f.Recoveries[0], f.Context, default);
        Assert.True((await ManagerWorkRecovery.HandleAsync(f.Manager, f.Recoveries[0], f.Context, default))!.IsCompleted);
        f.Recoveries[0] = f.Recoveries[0] with { Status = PersonalTodoStatuses.Completed };
        f.Work = f.Work with { Status = PersonalTodoStatuses.Blocked, Revision = f.Work.Revision + 1 };
        await ManagerWorkRecovery.DiscoverAsync(f.Context, default);
        var result = await ManagerWorkRecovery.HandleAsync(f.Manager, f.Recoveries[1], f.Context, default);
        Assert.Equal(2, f.Recoveries.Count); // A new failure episode has an owner, not another execution grant.
        Assert.Equal(1, f.Retries); Assert.Equal(1, f.Manager.Assessments);
        Assert.Contains("still blocked after the reviewed retry", result!.Content); Assert.Null(result.NextReviewAt);
    }

    [Fact]
    public async Task DriftDuringReviewCannotRequeueDifferentOrRevisedWork()
    {
        var f = new Fixture(); await ManagerWorkRecovery.DiscoverAsync(f.Context, default);
        f.Manager.DuringAssessment = () => f.Work = f.Work with { Revision = f.Work.Revision + 1 };
        await ManagerWorkRecovery.HandleAsync(f.Manager, f.Recoveries[0], f.Context, default);
        Assert.Equal(0, f.Retries);
    }

    private sealed class Manager : CSweetManagerAgentBase
    {
        public int Assessments; public bool SawPriorRetry; public System.Action? DuringAssessment; public string Action = "Retry";
        public override string AgentId => "future.manager";
        public override string Version => "1.0.0";
        protected override string ManagementResponsibility => "Current assigned work";
        public override Task<WorkRecoveryAssessment> AssessWorkRecoveryAsync(ManagerWorkRecoveryContext recovery, AgentRuntimeContext context, CancellationToken token)
        {
            Assessments++; SawPriorRetry |= recovery.PreviousRetry; DuringAssessment?.Invoke();
            return Task.FromResult(new WorkRecoveryAssessment(Action, "Reviewed current authority and retained source; coordinate the existing task."));
        }
    }

    private sealed class Fixture
    {
        public Guid Self { get; } = Guid.NewGuid();
        private Guid Owner { get; } = Guid.NewGuid();
        public bool ReportsHere = true, LoseStateResponse, LoseAddResponse, LoseRetryResponse, FailBeforeRetry, LoseReviewResponse;
        public int Retries, TrackerWakes;
        public PersonalTodoItem Work;
        public List<PersonalTodoItem> Recoveries { get; } = [];
        public Manager Manager { get; } = new();
        public AgentRuntimeContext Context { get; }
        public Fixture()
        {
            Work = JsonSerializer.Deserialize<PersonalTodoItem>("{}")! with { Id = Guid.NewGuid(), BoardId = Guid.NewGuid(),
                OwnerOrganizationUserId = Owner, Title = "Resume retained planning", Status = PersonalTodoStatuses.Blocked,
                IsExecutable = true, Revision = 7, BlockReason = "Recorded platform authorization failure" };
            var states = new Dictionary<string, AgentOperatingStateResponse>();
            var runtime = new AgentTestRuntime()
                .RegisterCapability<object, PersonalTodoDirectory>(PersonalTodoCapabilities.Read, (_, _) => Task.FromResult(new PersonalTodoDirectory([
                    new(Guid.NewGuid(), Self, "Manager", null, null, 1, Recoveries),
                    new(Work.BoardId, Owner, "Producer", ReportsHere ? Self : Guid.NewGuid(), "Manager", 1, [Work])], Self)))
                .RegisterCapability<AgentOperatingStateReadRequest, AgentOperatingStateReadResponse>(PlatformCapabilities.AgentOperatingStateRead,
                    (r, _) => Task.FromResult(new AgentOperatingStateReadResponse(states.GetValueOrDefault(r.StateKey))))
                .RegisterCapability<AgentOperatingStateWriteRequest, AgentOperatingStateResponse>(PlatformCapabilities.AgentOperatingStateWrite, (r, _) => {
                    var now = DateTimeOffset.UtcNow;
                    var saved = new AgentOperatingStateResponse(Guid.NewGuid(), r.StateKey, r.SchemaId, r.SchemaVersion, r.Status,
                        r.SourceRevisions, r.ConditionCodes, r.DecisionFingerprint, r.OpenCommitmentCorrelations, r.AttentionReviewId,
                        r.Payload, (states.GetValueOrDefault(r.StateKey)?.Revision ?? 0) + 1, now, now);
                    states[r.StateKey] = saved;
                    if (LoseStateResponse) { LoseStateResponse = false; throw new InvalidOperationException("Response lost after state commit"); }
                    if (LoseReviewResponse && r.Payload.TryGetProperty("reviewedBasis", out var reviewed) && reviewed.ValueKind == JsonValueKind.String) {
                        LoseReviewResponse = false; throw new InvalidOperationException("Response lost after atomic review/intent commit"); }
                    return Task.FromResult(saved);
                })
                .RegisterCapability<AddPersonalTodoItemRequest, PersonalTodoItem>(PersonalTodoCapabilities.Add, (r, _) => {
                    var item = Recoveries.SingleOrDefault(t => t.CorrelationId == r.CorrelationId);
                    if (item is null) { item = Work with { Id = Guid.NewGuid(), OwnerOrganizationUserId = Self, CorrelationId = r.CorrelationId,
                        WorkContext = r.WorkContext, Status = PersonalTodoStatuses.Ready, Revision = 1 }; Recoveries.Add(item); }
                    if (LoseAddResponse) { LoseAddResponse = false; throw new InvalidOperationException("Response lost after Add"); }
                    return Task.FromResult(item);
                })
                .RegisterCapability<RequeuePersonalTodoItemRequest, PersonalTodoItem>(PersonalTodoCapabilities.Requeue, (r, _) => {
                    var tracker = Recoveries.SingleOrDefault(t => t.Id == r.ItemId);
                    if (tracker is not null) { TrackerWakes++; tracker = tracker with { Status = PersonalTodoStatuses.Ready, Wait = null };
                        Recoveries[Recoveries.FindIndex(t => t.Id == tracker.Id)] = tracker; return Task.FromResult(tracker); }
                    if (FailBeforeRetry) { FailBeforeRetry = false; throw new InvalidOperationException("Interrupted before dispatch"); }
                    Assert.Equal(Work.Id, r.ItemId); Assert.Equal(Work.Revision, r.ExpectedRevision);
                    Retries++; Work = Work with { Status = PersonalTodoStatuses.Ready, Revision = Work.Revision + 1 };
                    if (LoseRetryResponse) { LoseRetryResponse = false; throw new InvalidOperationException("Response lost after retry"); }
                    return Task.FromResult(Work);
                });
            Context = runtime.CreateContext(identity: new(Self.ToString(), "Manager", null, null, null, [], null, null, null));
        }
    }
}
