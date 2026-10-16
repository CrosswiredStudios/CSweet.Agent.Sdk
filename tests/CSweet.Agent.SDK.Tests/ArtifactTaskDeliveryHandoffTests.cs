using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CSweet.WorkManagement.Contracts;

namespace CSweet.Agent.SDK.Tests;

public sealed class ArtifactTaskDeliveryHandoffTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task ActualDocumentCreationSubmissionPersistenceAndIndependentReviewUseTheSameContract(bool ambiguousHistory, bool olderFirst)
    {
        var authorInstallation = Guid.NewGuid(); var reviewerInstallation = Guid.NewGuid();
        var artifact = Guid.NewGuid(); var revision = Guid.NewGuid(); var now = DateTimeOffset.UtcNow;
        var content = "## API validation\n" + new string('a', 240);
        var sha = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(content)));
        var planning = new WorkItemPlanningSpecification(["Validate runtime API"], ["Provide exact API evidence"], []);
        var item = JsonSerializer.Deserialize<WorkItem>("{}")! with
        {
            Id = Guid.NewGuid(), Title = "Runtime validation", PlanningRevision = 1, Planning = planning,
            Delivery = new(Guid.Empty, [], [], []) { DeliveryKind = "Artifact", DeliveryPlanId = Guid.NewGuid() },
            StageAssignments = [new("development", "AgentInstallation", Guid.NewGuid(), authorInstallation),
                new("quality", "AgentInstallation", Guid.NewGuid(), reviewerInstallation)]
        };
        var assignment = JsonSerializer.Deserialize<WorkExecutionAssignment>("{}")! with
        {
            Scope = WorkExecutionScopes.Task, SprintId = Guid.NewGuid(), SprintExecutionId = Guid.NewGuid(), PolicyRevisionId = Guid.NewGuid(),
            OrganizationId = Guid.NewGuid(), BoardId = Guid.NewGuid(), ItemId = item.Id, StageExecutionId = Guid.NewGuid(), AttemptId = Guid.NewGuid(),
            StageKey = "development", Input = JsonSerializer.SerializeToElement(new WorkExecutionInputV1(Guid.NewGuid(), Guid.NewGuid(), 1, planning), Json),
            Instructions = "Deliver validation", PriorOutcomes = [], Evidence = [], Item = JsonSerializer.SerializeToElement(new { })
        };
        ArtifactDocument? saved = null; var submissions = 0; var inference = 0; var artifactReads = 0;
        var runtime = new AgentTestRuntime()
            .RegisterCapability<CreateArtifactDocument, ArtifactDocument>(PlatformCapabilities.ArtifactCreate, (request, _) =>
            {
                Assert.Equal(item.Id, request.OriginWorkItemId); Assert.Equal(content, request.Content);
                saved = new(artifact, item.Title, request.DocumentType, "Draft", revision, null, null, null, item.Id,
                    [new(revision, 1, null, request.Content, sha, "Draft", now, null, null)]);
                return Task.FromResult(saved);
            })
            .RegisterCapability<SubmitArtifactRevision, ArtifactDocument>(PlatformCapabilities.ArtifactSubmit, (request, _) =>
            {
                submissions++; Assert.Equal(artifact, request.ArtifactId); Assert.Equal(revision, request.RevisionId);
                saved = saved! with { Status = "Submitted", SubmittedRevisionId = revision };
                return Task.FromResult(saved);
            })
            .RegisterCapability<JsonElement, ArtifactDocument>(PlatformCapabilities.ArtifactRead, (request, _) =>
            {
                artifactReads++;
                Assert.Equal(artifact, request.GetProperty("artifactId").GetGuid());
                return Task.FromResult(saved!);
            })
            .RegisterCapability<WorkItemReference, WorkItem>(WorkItemCapabilities.Read, (_, _) => Task.FromResult(item))
            .RegisterCapability<JsonElement, JsonElement>(PlatformCapabilities.LlmChatStream, (request, _) =>
            {
                inference++;
                if (inference == 1) return Task.FromResult(JsonSerializer.SerializeToElement(new { text = content, role = "assistant" }));
                Assert.Contains("API names and feasibility assertions", request.GetProperty("messages")[0].GetProperty("text").GetString());
                var prompt = JsonSerializer.Deserialize<JsonElement>(request.GetProperty("messages").EnumerateArray().Last().GetProperty("text").GetString()!);
                Assert.Equal(content, prompt.GetProperty("content").GetString());
                Assert.Equal(revision, prompt.GetProperty("revisionId").GetGuid());
                Assert.Contains("Version-specific upstream source", prompt.GetProperty("reviewEvidence").GetRawText());
                var report = new WorkArtifactQualityResult(artifact, revision, sha, true, "Reviewed exact API evidence",
                    [new(planning.AcceptanceCriteria.Single(), true, "Document contains concrete API evidence")], []);
                return Task.FromResult(JsonSerializer.SerializeToElement(new { text = JsonSerializer.Serialize(report, Json), role = "assistant" }));
            });
        var authorContext = runtime.CreateContext(assignment.OrganizationId.ToString(), authorInstallation.ToString());
        var authored = await ArtifactTaskDelivery.ExecuteAsync(assignment, item, authorContext,
            authorContext.CreateChatClient(new(Guid.NewGuid(), "test")), default);
        Assert.Equal(1, submissions);
        var outcome = JsonSerializer.Deserialize<WorkExecutionOutcomeV1>(authored.Value!.Value.GetRawText(), Json)!;
        Assert.True(WorkArtifactDelivery.TryRead(outcome.Output, out var delivered));
        Assert.Equal(revision, delivered.RevisionId);
        var qa = assignment with
        {
            StageKey = "quality", StageExecutionId = Guid.NewGuid(), AttemptId = Guid.NewGuid(), PriorOutcomes = [outcome],
            Evidence = [new("manager-direction", "Verification evidence", "Version-specific upstream source: verify every claimed API; missing evidence is a finding.")],
            Item = JsonSerializer.SerializeToElement(new { deliverySpecificationJson = JsonSerializer.Serialize(item.Delivery, Json) })
        };
        if (ambiguousHistory)
        {
            var old = outcome with { StageExecutionId = Guid.NewGuid(), AttemptId = Guid.NewGuid(),
                Output = new WorkArtifactDelivery(Guid.NewGuid(), Guid.NewGuid(), sha).ToOutput() };
            qa = qa with { PriorOutcomes = olderFirst ? [old, outcome] : [outcome, old] };
        }
        qa = JsonSerializer.Deserialize<WorkExecutionAssignment>(JsonSerializer.Serialize(qa, Json), Json)!;
        var reviewContext = runtime.CreateContext(assignment.OrganizationId.ToString(), reviewerInstallation.ToString());
        if (ambiguousHistory)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => ArtifactDeliveryReview.ExecuteAsync(qa, reviewContext,
                reviewContext.CreateChatClient(new(Guid.NewGuid(), "test")), default));
            Assert.Equal(0, artifactReads); Assert.Equal(1, inference); return;
        }
        var reviewed = await ArtifactDeliveryReview.ExecuteAsync(qa, reviewContext,
            reviewContext.CreateChatClient(new(Guid.NewGuid(), "test")), default);
        var final = reviewed.Value!.Value.Deserialize<WorkExecutionOutcomeV1>(Json)!;
        Assert.Equal("passed", final.OutcomeCode); Assert.Equal(2, inference);
        var quality = final.Output.Deserialize<WorkArtifactQualityResult>(Json)!;
        Assert.Equal(artifact, quality.ArtifactId); Assert.Equal(revision, quality.RevisionId); Assert.Equal(sha, quality.Sha256);
    }
}
