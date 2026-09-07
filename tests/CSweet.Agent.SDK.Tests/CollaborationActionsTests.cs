using System.Text.Json;

namespace CSweet.Agent.SDK.Tests;

public sealed class CollaborationActionsTests
{
    private static CollaborationDocumentReference Document() => new(Guid.NewGuid(), Guid.NewGuid(), new string('a', 64));

    [Fact]
    public void DocumentationExampleProducesTypedBoardRequest()
    {
        var artifact = CollaborationActions.RequestDocumentation("project-42:requirements:v1",
            new DocumentationRequest("Prepare an implementation plan", "requirements.v1",
                ["Scope", "Constraints"], ["Acceptance criteria are testable"], []));
        var request = new StartBoardCoordinationRequest(Guid.NewGuid(), Guid.NewGuid(),
            "Requirements", "Agree on sufficient implementation context",
            ["Questions resolved", "Exact document revision reviewed"],
            "Please provide the requirements and identify missing decisions.",
            "project-42:requirements-session", artifact);
        Assert.Equal(CollaborationActions.DocumentationRequestType, request.Artifact!.Type);
        Assert.Equal("Prepare an implementation plan", artifact.Payload.GetProperty("purpose").GetString());
        Assert.Throws<ArgumentException>(() => CollaborationActions.RequestDocumentation("key",
            new("", "requirements.v1", ["scope"], ["testable"], [])));
    }

    [Fact]
    public void SharingPreservesDomainPayloadAndRejectsDuplicateOrMissingIdentity()
    {
        var source = new AgentCoordinationArtifactSubmission("domain.v1", "1.0", "key", 1, true,
            JsonSerializer.SerializeToElement(new { scope = "small" }));
        var reference = Document();
        var shared = CollaborationActions.WithDocuments(source, [reference]);
        Assert.Equal("small", shared.Payload.GetProperty("scope").GetString());
        Assert.Equal(reference.RevisionId, shared.Payload.GetProperty("documentReferences")[0].GetProperty("revisionId").GetGuid());
        Assert.False(source.Payload.TryGetProperty("documentReferences", out _));
        Assert.Throws<ArgumentException>(() => CollaborationActions.WithDocuments(source, [reference, reference]));
        Assert.Throws<ArgumentException>(() => CollaborationActions.ShareDocuments("key", [reference with { RevisionId = Guid.Empty }]));
        Assert.Throws<ArgumentException>(() => CollaborationActions.ShareDocuments("key", Enumerable.Range(0, 9).Select(_ => Document()).ToArray()));
    }

    [Fact]
    public void ClarificationAllowsPartialAnswersButRejectsUnknownAndDuplicateIds()
    {
        var request = new ClarificationRequest([new("platform", "Which platform?"), new("budget", "Which budget?")], []);
        var answer = CollaborationActions.AnswerClarification("answer", request, new([new("platform", "Browser")]));
        Assert.Single(answer.Payload.GetProperty("answers").EnumerateArray());
        Assert.Throws<ArgumentException>(() => CollaborationActions.AnswerClarification("key", request, new([new("other", "Unknown")])));
        Assert.Throws<ArgumentException>(() => CollaborationActions.AnswerClarification("key", request, new([new("platform", "A"), new("platform", "B")])));
    }

    [Fact]
    public void HandoffRequiresNoBlockersAndReviewOfSameRevision()
    {
        var reference = Document();
        var handoff = new CollaborationHandoff(reference, true, [], [], "Sufficient context");
        var decision = new CollaborationReviewDecision(reference, true, "Reviewed", []);
        Assert.True(CollaborationActions.CanAcceptHandoff(handoff, decision));
        Assert.False(CollaborationActions.CanAcceptHandoff(null, decision));
        var missing = reference with { DocumentId = Guid.Empty };
        Assert.False(CollaborationActions.CanAcceptHandoff(handoff with { Document = missing }, decision with { Document = missing }));
        Assert.False(CollaborationActions.CanAcceptHandoff(handoff with { OpenQuestions = ["Budget?"] }, decision));
        Assert.False(CollaborationActions.CanAcceptHandoff(handoff with { Blockers = ["Access"] }, decision));
        Assert.False(CollaborationActions.CanAcceptHandoff(handoff, decision with { Document = reference with { RevisionId = Guid.NewGuid() } }));
        Assert.Throws<ArgumentException>(() => CollaborationActions.Handoff("key", handoff with { OpenQuestions = ["Budget?"] }));
        Assert.Throws<ArgumentException>(() => CollaborationActions.ReviewDecision("key", decision with { RequestedChanges = ["Change scope"] }));
        Assert.False(CollaborationActions.ReviewDecision("key", decision).Payload.TryGetProperty("documentReferences", out _));
    }

    [Fact]
    public void WaitExampleDefersOnlyUnresolvedDependencies()
    {
        var waiting = CollaborationDependencies.WaitFor(
            [new CollaborationDependency("requirements-review", false, "Waiting for the requirements review", Guid.NewGuid())],
            DateTimeOffset.UtcNow.AddMinutes(5));
        Assert.NotNull(waiting);
        Assert.Contains("requirements-review", waiting.Content);
        Assert.NotNull(waiting.NextReviewAt);
        Assert.Null(CollaborationDependencies.WaitFor([new("ready", true, "Reviewed")], DateTimeOffset.UtcNow.AddMinutes(5)));
        Assert.Throws<ArgumentException>(() => CollaborationDependencies.WaitFor([new("same", true, "ok"), new("same", false, "waiting")], DateTimeOffset.UtcNow.AddMinutes(5)));
        Assert.Throws<ArgumentOutOfRangeException>(() => CollaborationDependencies.WaitFor([new("wait", false, "waiting")], DateTimeOffset.UtcNow.AddMinutes(-1)));
    }

    [Theory]
    [InlineData("Accepted", true, true)]
    [InlineData("Draft", true, false)]
    [InlineData("Accepted", false, false)]
    public async Task ExactSourceLookupVerifiesStatusAndDigest(string status, bool exact, bool succeeds)
    {
        var reference = Document();
        var revision = new ArtifactRevision(reference.RevisionId, 1, null, "content", reference.ContentSha256, status, DateTimeOffset.UtcNow, null, null);
        var document = new ArtifactDocument(reference.DocumentId, "Title", "requirements.v1", status,
            Guid.NewGuid(), null, reference.RevisionId, null, null, [revision]);
        var runtime = new AgentTestRuntime().RegisterCapability<JsonElement, ArtifactDocument>(PlatformCapabilities.ArtifactRead,
            (_, _) => Task.FromResult(document));
        var query = reference with { ContentSha256 = exact ? reference.ContentSha256 : "wrong" };
        if (succeeds) Assert.Equal(reference.RevisionId, (await runtime.CreateContext().Platform.Artifacts.ReadAcceptedAsync(query)).Revision.Id);
        else await Assert.ThrowsAsync<InvalidOperationException>(() => runtime.CreateContext().Platform.Artifacts.ReadAcceptedAsync(query));
    }
}
