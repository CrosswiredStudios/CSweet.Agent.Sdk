using System.Text.Json;
using CSweet.Agent.Testing;
using CSweet.WorkManagement.Contracts;

namespace CSweet.Agent.SDK.Tests;

public sealed class AgentTestHostTests
{
    private sealed class TurnAgent(string answer) : CSweetAgentBase
    {
        public override string AgentId => "com.example.turn";
        public override string Version => "1.0.0";
        public AgentAttentionReviewContext? Review { get; private set; }

        public override Task<AgentCoordinationTurnResult> HandleCoordinationTurnAsync(
            AgentCoordinationTurnRequest request, AgentRuntimeContext context, CancellationToken token) =>
            Task.FromResult(AgentCoordinationTurnResult.Continue(answer));

        public override Task HandleAttentionReviewAsync(
            AgentAttentionReviewContext review, AgentRuntimeContext context, CancellationToken token)
        {
            Review = review;
            return Task.CompletedTask;
        }
    }

    private static AgentCoordinationTurnRequest Request()
    {
        var self = new AgentCoordinationParticipant(Guid.NewGuid(), Guid.NewGuid(), "Reviewer", "Director");
        var peer = new AgentCoordinationParticipant(Guid.NewGuid(), Guid.NewGuid(), "Author", "Producer");
        return new(Guid.NewGuid(), 7, 2, "Pitch review", "Review the proposed scope", [], self, peer, false, []);
    }

    [Fact]
    public async Task Redelivered_turn_submits_the_same_revision_ordinal_and_domain_key()
    {
        var request = Request();
        var attempts = new List<RespondToAgentCoordinationRequest>();
        var stored = new Dictionary<string, RespondToAgentCoordinationRequest>();
        var session = new AgentCoordinationSession(request.SessionId, Guid.NewGuid(), Guid.Empty, Guid.Empty,
            Guid.Empty, request.Self, request.Counterpart, request.Subject, request.Objective, [], "Active",
            request.ExpectedRevision + 1, request.TurnOrdinal, null, false, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, []);
        var runtime = new AgentTestRuntime().RegisterCapability<RespondToAgentCoordinationRequest, AgentCoordinationSession>(
            CommunicationCapabilities.CoordinationRespond, (reply, _) =>
            {
                attempts.Add(reply);
                stored.TryAdd(reply.IdempotencyKey, reply);
                return Task.FromResult(session);
            });
        var host = new AgentTestHost(new TurnAgent("Which acceptance criterion covers the second player?"), runtime);
        var eventId = Guid.NewGuid();
        await host.DeliverCoordinationTurnAsync(request, eventId);
        await host.DeliverCoordinationTurnAsync(request, eventId);
        Assert.Equal(2, attempts.Count);
        Assert.Equal(attempts[0], attempts[1]);
        var accepted = Assert.Single(stored).Value;
        Assert.Equal(request.ExpectedRevision, accepted.ExpectedRevision);
        Assert.Equal(request.TurnOrdinal, accepted.ExpectedTurnOrdinal);
        Assert.Equal($"coordination-turn:{request.SessionId:N}:{request.TurnOrdinal}", accepted.IdempotencyKey);
    }

    [Theory]
    [InlineData("ok", false)]
    [InlineData("Which criterion covers both players?", true)]
    public async Task Invalid_coordination_answer_fails_before_submission(string answer, bool finalization)
    {
        var submissions = 0;
        var runtime = new AgentTestRuntime().RegisterCapability<RespondToAgentCoordinationRequest, JsonElement>(
            CommunicationCapabilities.CoordinationRespond, (_, _) => { submissions++; return Task.FromResult(default(JsonElement)); });
        var host = new AgentTestHost(new TurnAgent(answer), runtime);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            host.DeliverCoordinationTurnAsync(Request() with { IsFinalization = finalization }));
        Assert.Equal(0, submissions);
    }

    [Fact]
    public async Task Calendar_reminder_reaches_the_typed_callback()
    {
        var runtime = new AgentTestRuntime();
        var host = new AgentTestHost(new TurnAgent(""), runtime);
        var reminder = new CalendarReminderDueEvent(Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow,
            "Review the pitch", Guid.NewGuid());
        await host.DeliverEventAsync(CalendarEvents.ReminderDue, reminder);
        var received = Assert.Single(runtime.Progress).GetProperty("calendarReminder");
        Assert.Equal(reminder.ReminderId, received.GetProperty("reminderId").GetGuid());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Attention_distinguishes_an_explicit_claim_denial_from_a_missing_fixture(bool configureDenial)
    {
        var agent = new TurnAgent("");
        var runtime = new AgentTestRuntime();
        if (configureDenial)
            runtime.RegisterCapability<ClaimPersonalTodoItemRequest, PersonalTodoClaim>(PersonalTodoCapabilities.Claim,
                (_, _) => throw new PlatformCapabilityException(PersonalTodoCapabilities.Claim, PlatformCapabilityErrorCode.Denied, "No personal queue."));
        var host = new AgentTestHost(agent, runtime);
        var review = new AgentAttentionReviewDueEvent(Guid.NewGuid(), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMinutes(5), "Periodic")
            { TriggerCategory = "planning", CorrelationId = Guid.NewGuid() };
        Task<AgentWorkResult> Deliver() => host.DeliverEventAsync(AgentAttentionEvents.ReviewDue, review);
        if (configureDenial)
            Assert.True((await Deliver()).Succeeded);
        else
            Assert.Contains(PersonalTodoCapabilities.Claim, (await Assert.ThrowsAsync<InvalidOperationException>(Deliver)).Message);
        Assert.Equal(review.CorrelationId, agent.Review!.CorrelationId);
        Assert.Equal(review.TriggerCategory, agent.Review.TriggerCategory);
    }
}
