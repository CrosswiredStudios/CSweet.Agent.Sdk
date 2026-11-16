using System.Text.Json;
using CSweet.WorkManagement.Contracts;

namespace CSweet.Agent.SDK.Tests;

public sealed class WorkContributionLinksTests
{
    [Fact]
    public async Task SameContributionCanSupportEpicThenStoryWithoutChangingItsIdentity()
    {
        var f = new Fixture();
        Assert.True(await WorkContributionLinks.AssociateAsync(f.Link, f.Context));
        Assert.True(await WorkContributionLinks.AssociateAsync(f.Link, f.Context));
        var story = f.Link with { WorkItemId = Guid.NewGuid() };
        Assert.True(await WorkContributionLinks.AssociateAsync(story, f.Context));
        Assert.Equal(2, f.Comments.Count);
        Assert.All(f.Comments.Values, c => Assert.Equal(f.Link.ArtifactDigest, c.ArtifactDigest));
        Assert.All(f.Comments.Values, c => Assert.DoesNotContain("Full expert reasoning", c.Body));
    }

    [Fact]
    public async Task FailedProjectionCreatesOwnedRepairWithoutErasingEvidenceAndRepairRechecksSource()
    {
        var f = new Fixture { Denied = true };
        Assert.False(await WorkContributionLinks.AssociateAsync(f.Link, f.Context));
        Assert.NotNull(f.Repair);
        f.Denied = false;
        Assert.True((await WorkContributionLinks.HandleAsync(f.Repair!, f.Context))!.IsCompleted);
        f.Retracted = true;
        Assert.False((await WorkContributionLinks.HandleAsync(f.Repair!, f.Context))!.IsCompleted);
        Assert.Single(f.Comments);
    }

    [Fact]
    public async Task RetractedContributionCannotBeTreatedAsAnOptionalDisplayFailure()
    {
        var f = new Fixture { Retracted = true };
        await Assert.ThrowsAsync<PlatformCapabilityException>(() => WorkContributionLinks.AssociateAsync(f.Link, f.Context));
        Assert.Null(f.Repair); Assert.Empty(f.Comments);
    }

    private sealed class Fixture
    {
        public WorkContributionLink Link { get; } = new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "advice", "digest");
        public readonly Dictionary<string, CommentOnWorkItemRequest> Comments = [];
        public bool Denied, Retracted;
        public PersonalTodoItem? Repair;
        public AgentRuntimeContext Context { get; }
        public Fixture()
        {
            var states = new Dictionary<string, AgentOperatingStateResponse>();
            var runtime = new AgentTestRuntime()
                .RegisterCapability<object, AgentCoordinationSession>(CommunicationCapabilities.CoordinationRead, (_, _) => {
                    var expert = new AgentCoordinationParticipant(Link.AuthorEmployeeId, Guid.NewGuid(), "External specialist", "Expert");
                    var manager = new AgentCoordinationParticipant(Guid.NewGuid(), Guid.NewGuid(), "Manager", "Manager");
                    var session = JsonSerializer.Deserialize<AgentCoordinationSession>("{}")! with {
                        Id = Link.SessionId, ConversationId = Guid.NewGuid(), Initiator = manager, Target = expert,
                        Status = "Completed", BoardSource = new(Link.BoardId),
                        WorkContext = new(Guid.NewGuid(), Link.WorkstreamId, null, Link.BoardId, null, null, null, Guid.NewGuid(), null, null),
                        Turns = [new(Guid.NewGuid(), 1, Link.AuthorEmployeeId, Retracted ? "Blocked" : "Completed", "Full expert reasoning", DateTimeOffset.UtcNow,
                            new(WorkContributions.AnswerType, "1.0", Link.ArtifactKey, 1, true, JsonSerializer.SerializeToElement(new {}), Link.ArtifactDigest))]
                    };
                    return Task.FromResult(session);
                })
                .RegisterCapability<CommentOnWorkItemRequest, WorkItemComment>(WorkItemCapabilities.Comment, (r, _) => {
                    if (Denied) throw new PlatformCapabilityException(WorkItemCapabilities.Comment, PlatformCapabilityErrorCode.Denied, "Reference display currently denied.");
                    if (Comments.TryGetValue(r.IdempotencyKey, out var old)) Assert.Equal(old.ItemId, r.ItemId);
                    Comments[r.IdempotencyKey] = r;
                    return Task.FromResult(JsonSerializer.Deserialize<WorkItemComment>("{}")!);
                })
                .RegisterCapability<AgentOperatingStateReadRequest, AgentOperatingStateReadResponse>(PlatformCapabilities.AgentOperatingStateRead,
                    (r, _) => Task.FromResult(new AgentOperatingStateReadResponse(states.GetValueOrDefault(r.StateKey))))
                .RegisterCapability<AgentOperatingStateWriteRequest, AgentOperatingStateResponse>(PlatformCapabilities.AgentOperatingStateWrite, (r, _) => {
                    var now = DateTimeOffset.UtcNow;
                    var value = new AgentOperatingStateResponse(Guid.NewGuid(), r.StateKey, r.SchemaId, r.SchemaVersion, r.Status,
                        r.SourceRevisions, r.ConditionCodes, r.DecisionFingerprint, r.OpenCommitmentCorrelations, r.AttentionReviewId, r.Payload, 1, now, now);
                    states[r.StateKey] = value; return Task.FromResult(value);
                })
                .RegisterCapability<AddPersonalTodoItemRequest, PersonalTodoItem>(PersonalTodoCapabilities.Add, (r, _) => {
                    Repair = JsonSerializer.Deserialize<PersonalTodoItem>("{}")! with { Id = Guid.NewGuid(),
                        CorrelationId = r.CorrelationId, WorkContext = r.WorkContext };
                    return Task.FromResult(Repair);
                });
            Context = runtime.CreateContext();
        }
    }
}
