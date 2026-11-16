using System.Text.Json;
using CSweet.WorkManagement.Contracts;
using Microsoft.Extensions.AI;

namespace CSweet.Agent.SDK.Tests;

public sealed class WorkAvailabilityContextTests
{
    [Fact]
    public async Task AvailabilityUsesOwnCommitmentsAndCalendarAcrossBoardsWithoutCountingProposedWorkTwice()
    {
        var f = new Fixture();
        var context = await WorkAvailabilityContext.ReadAsync(f.Board, f.Owner, f.Installation, [f.Proposed], f.Context);
        var material = context.Facts.GetProperty("material");
        Assert.Equal("Own investigation", Assert.Single(material.GetProperty("personalCommitments").EnumerateArray()).GetProperty("Title").GetString());
        Assert.Equal("Other committed work", Assert.Single(material.GetProperty("competingAssignments").EnumerateArray()).GetProperty("Title").GetString());
        Assert.Equal("Own booking", Assert.Single(material.GetProperty("calendar").EnumerateArray()).GetProperty("Title").GetString());
        Assert.DoesNotContain("Another person's", context.Facts.GetRawText());
        var unchanged = await WorkAvailabilityContext.ReadAsync(f.Board, f.Owner, f.Installation, [f.Proposed], f.Context);
        Assert.Equal(context.Fingerprint, unchanged.Fingerprint);
        f.CalendarRevision++;
        var changed = await WorkAvailabilityContext.ReadAsync(f.Board, f.Owner, f.Installation, [f.Proposed], f.Context);
        Assert.NotEqual(context.Fingerprint, changed.Fingerprint);
    }

    [Fact]
    public async Task CachedLegacyZeroIsNotReplayedAndChangedAvailabilityGetsFreshOwnerJudgment()
    {
        var f = new Fixture();
        var input = JsonSerializer.SerializeToElement(new { f.BoardId, RoleKey = "operations-analyst", PlanningRevision = 1,
            PlanningDigest = "accepted", RequestFingerprint = "request", WorkItems = new[] { new { WorkItemId = f.Proposed, outcome = "Research note" } } });
        var oldKey = WorkPlanningJudgments.EstimateStateKey("request", input);
        f.States[oldKey] = f.State(oldKey, JsonSerializer.SerializeToElement(new WorkPlanningJudgments.Assessment([new(8, "medium", "Research")], 0, ["No calendar allocation supplied"], [])));
        using var client = new Replies();
        var first = await WorkPlanningJudgments.EstimateAsync(input, "request", f.Context, client, null, default);
        var replay = await WorkPlanningJudgments.EstimateAsync(input, "request", f.Context, client, null, default);
        Assert.Equal(1, client.Calls);
        Assert.Equal(5, WorkCapacityAssessment.FromEvidence(first.Artifact!.Payload, 5).OfferedPoints);
        Assert.Equal(first.Artifact.Payload.GetRawText(), replay.Artifact!.Payload.GetRawText());
        f.CalendarRevision++;
        var changed = await WorkPlanningJudgments.EstimateAsync(input, "request", f.Context, client, null, default);
        Assert.Equal(2, client.Calls);
        Assert.Equal("Unavailable", WorkCapacityAssessment.FromEvidence(changed.Artifact!.Payload, 0).State);
        Assert.NotEqual(first.Artifact.Payload.GetProperty("AssessmentStateKey").GetString(), changed.Artifact.Payload.GetProperty("AssessmentStateKey").GetString());
        Assert.True(f.States.ContainsKey(oldKey));
        Assert.Contains("Own booking", client.LastInput);
        Assert.DoesNotContain("Another person's", client.LastInput);
    }

    private sealed class Fixture
    {
        public Guid Board { get; } = Guid.NewGuid();
        public Guid BoardId => Board;
        public Guid Owner { get; } = Guid.NewGuid();
        public Guid Installation { get; } = Guid.NewGuid();
        public Guid Proposed { get; } = Guid.NewGuid();
        private Guid OtherBoard { get; } = Guid.NewGuid();
        private Guid OtherOwner { get; } = Guid.NewGuid();
        private Guid Booking { get; } = Guid.NewGuid();
        public long CalendarRevision { get; set; } = 1;
        public Dictionary<string, AgentOperatingStateResponse> States { get; } = [];
        public AgentRuntimeContext Context { get; }

        public Fixture()
        {
            WorkBoardSummary BoardSummary(Guid id) => new(id, "Operations", "", false, false, 1, []);
            WorkItem Work(Guid id, Guid owner, string title) => new(id, Guid.NewGuid(), null, null, "Task", title, "", "Running", "High", 3, 1, 1, null,
                AssignedEmployeeId: owner);
            PersonalTodoItem Todo(Guid owner, string title) => JsonSerializer.Deserialize<PersonalTodoItem>("{}")! with {
                Id = Guid.NewGuid(), OwnerOrganizationUserId = owner, Title = title, Status = "Running", IsExecutable = true };
            var own = Todo(Owner, "Own investigation"); var other = Todo(OtherOwner, "Another person's investigation");
            var agenda = new PersonalTodoDirectory([new(Guid.NewGuid(), Owner, "Owner", null, null, 1, [own]),
                new(Guid.NewGuid(), OtherOwner, "Other", null, null, 1, [other])], Owner);
            var runtime = new AgentTestRuntime()
                .RegisterCapability<WorkBoardListRequest, IReadOnlyList<WorkBoardSummary>>(WorkBoardCapabilities.Read,
                    (_, _) => Task.FromResult<IReadOnlyList<WorkBoardSummary>>([BoardSummary(Board), BoardSummary(OtherBoard)]))
                .RegisterCapability<WorkBoardReference, WorkBoardDetail>(WorkItemCapabilities.Read, (r, _) => Task.FromResult(new WorkBoardDetail(BoardSummary(r.BoardId), [],
                    r.BoardId == Board ? [Work(Proposed, Owner, "Proposed work")] : [Work(OtherBoard, Owner, "Other committed work"), Work(OtherOwner, OtherOwner, "Another person's assignment")])))
                .RegisterCapability<object, PersonalTodoDirectory>(PersonalTodoCapabilities.Read, (_, _) => Task.FromResult(agenda))
                .RegisterCapability<CalendarQuery, BusinessCalendarView>(CalendarCapabilities.Read, (r, _) => {
                    Assert.True(r.To > r.From);
                    CalendarOccurrence Occurrence(Guid id, Guid owner, string title) {
                        var input = new CalendarEventInput(title, r.From.UtcDateTime.AddHours(1), r.From.UtcDateTime.AddHours(2));
                        return new(id, input.StartLocal, r.From.AddHours(1), r.From.AddHours(2), new(id, CalendarRevision, owner, input, false, true), input);
                    }
                    return Task.FromResult(new BusinessCalendarView("UTC", CalendarRevision, Owner, true, true, [],
                        [Occurrence(Booking, Owner, "Own booking"), Occurrence(OtherOwner, OtherOwner, "Another person's booking")]));
                })
                .RegisterCapability<AgentOperatingStateReadRequest, AgentOperatingStateReadResponse>(PlatformCapabilities.AgentOperatingStateRead,
                    (r, _) => Task.FromResult(new AgentOperatingStateReadResponse(States.GetValueOrDefault(r.StateKey))))
                .RegisterCapability<AgentOperatingStateWriteRequest, AgentOperatingStateResponse>(PlatformCapabilities.AgentOperatingStateWrite,
                    (r, _) => Task.FromResult(States[r.StateKey] = State(r.StateKey, r.Payload)));
            Context = runtime.CreateContext(installationId: Installation.ToString(), identity: new(Owner.ToString(), "Owner", null, null, null, [], null, null, null));
        }

        public AgentOperatingStateResponse State(string key, JsonElement payload) => new(Guid.NewGuid(), key, "test", 1, "Active", new Dictionary<string, string>(), [], "test", [],
            Guid.NewGuid(), payload, 1, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
    }

    private sealed class Replies : IChatClient
    {
        public int Calls { get; private set; }
        public string LastInput { get; private set; } = "";
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            LastInput = messages.Last().Text!;
            var points = Calls++ == 0 ? 5 : 0;
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, JsonSerializer.Serialize(new {
                estimates = new[] { new { points = 8, confidence = "medium", rationale = "Research uncertainty" } },
                capacityAssessment = new { offeredPoints = points, rationale = "Current independently reviewed commitments" }, assumptions = Array.Empty<string>(), blockers = Array.Empty<string>() }))));
        }
        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }
}
