using CSweet.WorkManagement.Contracts;

namespace CSweet.Agent.SDK.Tests;

public sealed class TrackedCollaborationTests
{
    private static readonly AgentCoordinationParticipant Director =
        new(Guid.NewGuid(), Guid.NewGuid(), "Marcus Vale", "Video Game Director");
    private static readonly AgentCoordinationParticipant Producer =
        new(Guid.NewGuid(), Guid.NewGuid(), "Gabriel Reyes", "Producer");

    [Fact]
    public void BlockedSessionNamesTheOtherParticipantAsOwnerOfTheNextStep()
    {
        var session = Session(AgentCoordinationStatuses.Blocked, "The staffing roles\n do not match the template",
            new AgentCoordinationTurn(Guid.NewGuid(), 1, Producer.OrganizationUserId,
                AgentCoordinationDispositions.Blocked, "I cannot continue.", DateTimeOffset.UtcNow));

        var blocker = CollaborationBlocker.FromSession(session);

        Assert.NotNull(blocker);
        Assert.Equal(CollaborationBlockerReasons.ParticipantBlocked, blocker!.ReasonCode);
        Assert.Equal(Director.OrganizationUserId, blocker.NextStepOwnerOrganizationUserId);
        Assert.True(blocker.RetryUseful);
        Assert.Equal("The staffing roles do not match the template.", blocker.WhatMustChange);
        Assert.Equal(
            "The staffing roles do not match the template. Next step: Marcus Vale. Move this card back to To Do to retry. " +
            "[collaboration.participant-blocked]",
            blocker.ToCardReason());
    }

    [Fact]
    public void ActiveAndCompletedSessionsHaveNoBlockerAndCancelledIsNotRetryable()
    {
        Assert.Null(CollaborationBlocker.FromSession(Session(AgentCoordinationStatuses.Active, null)));
        Assert.Null(CollaborationBlocker.FromSession(Session(AgentCoordinationStatuses.Completed, "Done.")));
        var cancelled = CollaborationBlocker.FromSession(Session(AgentCoordinationStatuses.Cancelled, null));
        Assert.Equal(CollaborationBlockerReasons.Cancelled, cancelled!.ReasonCode);
        Assert.False(cancelled.RetryUseful);
        Assert.Contains("Retrying will not help", cancelled.ToCardReason(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task TestRuntimeFailsUntilTheStartedCollaborationHasAnOwningCard()
    {
        var boardId = Guid.NewGuid();
        var teamId = Guid.NewGuid();
        var session = Session(AgentCoordinationStatuses.Active, null) with { BoardSource = new(boardId), SourceKind = "Board" };
        AddPersonalTodoItemRequest? added = null;
        var runtime = new AgentTestRuntime()
            .RegisterCapability<StartBoardCoordinationRequest, AgentCoordinationSession>(
                CommunicationCapabilities.CoordinationStartBoard, (_, _) => Task.FromResult(session))
            .RegisterCapability<AddPersonalTodoItemRequest, PersonalTodoItem>(
                PersonalTodoCapabilities.Add,
                (request, _) => { added = request; return Task.FromResult(Card(request.CorrelationId)); });
        var context = runtime.CreateContext();

        var started = await context.Platform.Communication.StartBoardCoordinationAsync(new StartBoardCoordinationRequest(
            Producer.OrganizationUserId, boardId, "Project proposal", "Prepare the proposal.", ["Approved."],
            "Please prepare the proposal.", "start-1"));

        Assert.Equal(new[] { session.Id }, runtime.UntrackedCollaborations);
        var failure = Assert.Throws<InvalidOperationException>(runtime.AssertCollaborationsTracked);
        Assert.Contains("TrackedCollaboration.QueueCardAsync", failure.Message, StringComparison.Ordinal);

        var card = await TrackedCollaboration.QueueCardAsync(context, started,
            new TrackedCollaborationCard("Get the project proposal approved", "Tracks the proposal.") { TeamId = teamId });

        Assert.NotNull(card);
        Assert.Equal(TrackedCollaboration.Correlation(session.Id), added!.CorrelationId);
        Assert.Equal(TrackedCollaboration.Correlation(session.Id), added.IdempotencyKey);
        Assert.Equal(boardId, added.WorkContext!.BoardId);
        Assert.Equal(teamId, added.WorkContext.TeamId);
        Assert.Equal(session.Id, added.WorkContext.CoordinationSessionId);
        Assert.Empty(runtime.UntrackedCollaborations);
        runtime.AssertCollaborationsTracked();
    }

    [Fact]
    public async Task MissingPersonalTodoGrantLeavesTheCollaborationRunningAndUntracked()
    {
        var session = Session(AgentCoordinationStatuses.Active, null);
        var runtime = new AgentTestRuntime();

        var card = await TrackedCollaboration.QueueCardAsync(runtime.CreateContext(), session,
            new TrackedCollaborationCard("Track it", "Tracks it."));

        Assert.Null(card);
    }

    [Fact]
    public async Task ReviewFollowsTheSessionAndTheKnownOutcome()
    {
        var blocked = Session(AgentCoordinationStatuses.Blocked, "A budget figure is missing.");
        var active = Session(AgentCoordinationStatuses.Active, null);
        var runtime = new AgentTestRuntime().RegisterCapability<ReadAgentCoordinationRequest, AgentCoordinationSession>(
            CommunicationCapabilities.CoordinationRead,
            (request, _) => Task.FromResult(request.SessionId == blocked.Id ? blocked : active));
        var context = runtime.CreateContext();

        var blockedResult = await TrackedCollaboration.ReviewAsync(context,
            Card(TrackedCollaboration.Correlation(blocked.Id)), new TrackedCollaborationReview(false));
        var waiting = await TrackedCollaboration.ReviewAsync(context,
            Card(TrackedCollaboration.Correlation(active.Id)), new TrackedCollaborationReview(false));
        var reached = await TrackedCollaboration.ReviewAsync(context,
            Card(TrackedCollaboration.Correlation(blocked.Id)), new TrackedCollaborationReview(true));
        var replaced = await TrackedCollaboration.ReviewAsync(context,
            Card(TrackedCollaboration.Correlation(blocked.Id)),
            new TrackedCollaborationReview(false) { CurrentSessionId = active.Id });
        var invalid = await TrackedCollaboration.ReviewAsync(context, Card("something-else"),
            new TrackedCollaborationReview(false));

        Assert.False(blockedResult.IsCompleted);
        Assert.False(blockedResult.KeepInProgress);
        Assert.StartsWith("A budget figure is missing.", blockedResult.Content, StringComparison.Ordinal);
        Assert.True(waiting.KeepInProgress);
        Assert.NotNull(waiting.NextReviewAt);
        Assert.True(reached.IsCompleted);
        Assert.True(replaced.IsCompleted);
        Assert.False(invalid.IsCompleted);
        Assert.False(invalid.KeepInProgress);
    }

    private static AgentCoordinationSession Session(string status, string? summary, params AgentCoordinationTurn[] turns)
    {
        var now = DateTimeOffset.UtcNow;
        return new AgentCoordinationSession(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            Director, Producer, "Project proposal", "Prepare the proposal.", ["Approved."],
            status, 1, turns.Length + 1, Producer.OrganizationUserId, false, summary, now, now, turns);
    }

    private static PersonalTodoItem Card(string? correlation)
    {
        var owner = Director.OrganizationUserId;
        return new PersonalTodoItem(
            Guid.NewGuid(), Guid.NewGuid(), owner, owner, "Marcus Vale", "Track it", "",
            PersonalTodoStatuses.Ready, WorkPriorities.High, 1024, 1, null, null, null, [], null,
            null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow) { CorrelationId = correlation };
    }
}
