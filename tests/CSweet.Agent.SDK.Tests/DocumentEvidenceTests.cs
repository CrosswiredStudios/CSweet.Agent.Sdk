using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CSweet.WorkManagement.Contracts;
using Microsoft.Extensions.AI;

namespace CSweet.Agent.SDK.Tests;

public sealed class DocumentEvidenceTests
{
    [Theory]
    [InlineData("Accepted", false)] [InlineData("Draft", false)] [InlineData("Accepted", true)]
    public async Task EvidenceRequiresTheExactAcceptedContentRatherThanAnIdentityLabel(string status, bool tampered)
    {
        var text = "Approved workflow requirement with exact source content.";
        var sha = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
        var artifact = Guid.NewGuid(); var revision = Guid.NewGuid(); var package = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var runtime = new AgentTestRuntime().RegisterCapability<JsonElement, ArtifactDocument>(PlatformCapabilities.ArtifactRead,
            (_, _) => Task.FromResult(new ArtifactDocument(artifact, "Scope", "generic.scope.v1", status, revision, revision, revision, null, null,
                [new(revision, 1, null, tampered ? text + " altered" : text, sha, status, now, now, now)])));
        var members = new[] { new ArtifactPackageMemberDigest(artifact, revision, "Scope", sha) };
        var planning = new WorkItemPlanningSpecification([], [], []) { ArtifactPackageDigest = new(package, 1,
            ArtifactPackageDigestCalculator.Calculate(package, 1, members), now, members) };
        if (status != "Accepted" || tampered)
            await Assert.ThrowsAsync<InvalidOperationException>(() => DocumentEvidence.ReadAsync(planning, runtime.CreateContext(), default));
        else Assert.Equal(text, Assert.Single(await DocumentEvidence.ReadAsync(planning, runtime.CreateContext(), default)).Content);
    }

    [Fact]
    public void RetainsTheActualFailedReviewFindingsInsteadOfTheFullReworkHistory()
    {
        var review = JsonSerializer.Deserialize<WorkExecutionOutcomeV1>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
            "Fixtures", "shardfall-document-review.json")), JsonSerializerOptions.Web)!;
        var feedback = JsonSerializer.Serialize(DocumentEvidence.CurrentFeedback([review]), JsonSerializerOptions.Web);
        Assert.Contains(review.Summary, JsonSerializer.Deserialize<JsonElement>(feedback).GetProperty("summary").GetString());
        Assert.Contains("criteria", feedback, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("stageExecutionId", feedback);
    }

    [Fact]
    public async Task ResearchOffersOnlyGrantedEvidenceToolsAndBoundsPageReads()
    {
        var calls = 0;
        var runtime = new AgentTestRuntime()
            .RegisterCapability<WebPageRequest, WebPageResult>(ResearchCapabilities.Read, (r, _) => {
                calls++; Assert.Equal(12000, r.MaximumCharacters);
                return Task.FromResult(new WebPageResult(r.Url, "Primary documentation", "Observed authoritative evidence", false));
            })
            .RegisterCapability<JsonElement, JsonElement>("calendar.event.create.v1", (_, _) => throw new Exception("Calendar must not be offered"));
        var research = new DocumentResearch(runtime.CreateContext());
        var function = Assert.IsAssignableFrom<AIFunction>(Assert.Single(await research.ToolsAsync(default)));
        Assert.Equal("read_public_evidence", function.Name);
        for (var i = 0; i < 8; i++)
            await function.InvokeAsync(new AIFunctionArguments { ["url"] = "https://example.org/docs/" + i });
        await function.InvokeAsync(new AIFunctionArguments { ["url"] = "https://example.org/docs/0" });
        Assert.Equal(6, calls); Assert.Equal(6, research.Pages.Count);
    }

    [Fact]
    public async Task ReworkRevisesTheRetainedDocumentAndPublishesCodeOwnedIdentity()
    {
        var artifact = Guid.NewGuid(); var oldRevision = Guid.NewGuid(); var next = Guid.NewGuid(); var project = Guid.NewGuid(); var team = Guid.NewGuid();
        var assignment = JsonSerializer.Deserialize<WorkExecutionAssignment>("{}")! with { Scope = WorkExecutionScopes.Task, SprintId = Guid.NewGuid(), SprintExecutionId = Guid.NewGuid(), PolicyRevisionId = Guid.NewGuid(), ItemId = Guid.NewGuid(), StageExecutionId = Guid.NewGuid(), AttemptId = Guid.NewGuid() };
        var content = "Addressed the concrete source-evidence finding.";
        var sha = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(content)));
        var document = new ArtifactDocument(artifact, "Retained work", "generic.report.v1", "InReview", oldRevision, oldRevision, null, null, assignment.TaskItemId, []) { WorkstreamId = project, TeamId = team };
        var submissions = 0;
        var runtime = new AgentTestRuntime()
            .RegisterCapability<JsonElement, ArtifactDocument>(PlatformCapabilities.ArtifactRead, (_, _) => Task.FromResult(document))
            .RegisterCapability<CreateArtifactRevision, ArtifactRevision>(PlatformCapabilities.ArtifactRevise, (r, _) => {
                Assert.Equal(artifact, r.ArtifactId); Assert.Equal(content, r.Content);
                return Task.FromResult(new ArtifactRevision(next, 2, oldRevision, content, sha, "Draft", DateTimeOffset.UtcNow, null, null));
            })
            .RegisterCapability<SubmitArtifactRevision, ArtifactDocument>(PlatformCapabilities.ArtifactSubmit, (r, _) => {
                submissions++; Assert.Equal(artifact, r.ArtifactId); Assert.Equal(next, r.RevisionId); return Task.FromResult(document);
            });
        var published = await DocumentWork.PublishAsync(assignment, "Rework", document.DocumentType, new(content, [], null), project, team,
            runtime.CreateContext(), default, artifact);
        Assert.Equal(artifact, published.Document.Id); Assert.Equal(next, published.Revision.Id); Assert.Equal(1, submissions);
        await Assert.ThrowsAsync<InvalidOperationException>(() => DocumentWork.PublishAsync(assignment with { ItemId = Guid.NewGuid() },
            "Foreign work", document.DocumentType, new(content, [], null), project, team, runtime.CreateContext(), default, artifact));
    }
}
