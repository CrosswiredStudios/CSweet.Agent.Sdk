using System.Text.Json;
using Microsoft.Extensions.AI;
using CSweet.WorkManagement.Contracts;

namespace CSweet.Agent.SDK.Tests;

public sealed class BranchDiffTechnicalReviewTests
{
    [Fact]
    public async Task TaskCallbackUsesReferenceThenBoundedDiffUnderExistingGrant()
    {
        var reviewer = Guid.NewGuid();
        var reference = Reference("src/game.ts");
        var item = JsonSerializer.Deserialize<WorkItem>("{}")! with
        {
            Id = reference.WorkItemId, PlanningRevision = 1,
            Delivery = new(reference.RepositoryId, ["Works"], ["Works"])
                { DeliveryPlanId = Guid.NewGuid(), BaseBranch = "story" },
            StageAssignments = [new("technical-review", "AgentInstallation", AgentInstallationId: reviewer)]
        };
        var calls = new List<ReviewGitMergeRequest>();
        var runtime = new AgentTestRuntime()
            .RegisterCapability<WorkItemReference, WorkItem>(WorkManagementCapabilityNames.ItemRead, (_, _) => Task.FromResult(item))
            .RegisterCapability<ReviewGitMergeRequest, GitMergeReview>(GitMergeCapabilities.Review, (request, _) =>
            {
                calls.Add(request);
                Assert.False(request.IncludePatch);
                return Task.FromResult(request.DiffRead is null ? reference :
                    reference with { DiffPage = new(Digest, 0, 3, "abc", null) });
            });
        var assignment = JsonSerializer.Deserialize<WorkExecutionAssignment>("{}")! with
        {
            Scope = WorkExecutionScopes.Task, ItemId = item.Id, BoardId = Guid.NewGuid(),
            SprintId = Guid.NewGuid(), SprintExecutionId = Guid.NewGuid(), PolicyRevisionId = Guid.NewGuid(),
            StageKey = "technical-review", StageExecutionId = Guid.NewGuid(), AttemptId = Guid.NewGuid(), AssignmentRevision = 1
        };
        var client = new Client(number => number == 0
            ? JsonSerializer.Serialize(new { summary = "Reviewed", findings = Array.Empty<string>() })
            : JsonSerializer.Serialize(new { approved = true, summary = "Verified source", findings = Array.Empty<string>() }));
        var result = await DeliveryTaskTechnicalReview.ExecuteAsync(assignment,
            runtime.CreateContext(installationId: reviewer.ToString()), client, default);
        var outcome = result.Value!.Value.Deserialize<WorkExecutionOutcomeV1>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.Equal("approved", outcome.OutcomeCode);
        Assert.Equal(2, calls.Count);
        Assert.Null(calls[0].DiffRead);
        Assert.Equal(reference.PublicationId, calls[1].DiffRead!.PublicationId);
        Assert.Contains(outcome.Evidence, x => x.Value == Source);
        Assert.Contains(outcome.Evidence, x => x.Value == Target);
    }

    [Theory]
    [InlineData("node_modules/phaser/index.js", true)]
    [InlineData("dist/assets/game.js", true)]
    [InlineData("app/.next/server.js", true)]
    [InlineData(".venv/lib/dependency.py", true)]
    [InlineData("package-lock.json", false)]
    [InlineData("src/bin/launcher.ts", false)]
    [InlineData("assets/player.png", false)]
    public void SourcePolicyKeepsAuthoredFilesAndLockfiles(string path, bool excluded) =>
        Assert.Equal(excluded, RepositorySourcePolicy.IsGeneratedOrDependencyPath(path));

    private static readonly string Source = new('a', 40), Target = new('b', 40), Digest = new('c', 64);
    private static GitMergeReview Reference(params string[] files) => new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
        "Game", Source, null, "", [], [], "AwaitingValidation")
        { TargetCommitSha = Target, TargetBranch = "story", PatchDigest = Digest, ChangedFiles = files };

    [Fact]
    public async Task GeneratedLibraryBundleRequestsSourceCleanupWithoutSendingItToModel()
    {
        var client = new Client(_ => throw new InvalidOperationException("Must not invoke model."));
        var result = await BranchDiffTechnicalReview.ExecuteAsync(Reference("src/game.ts", "dist/assets/engine.js"), new { },
            (_, _) => throw new InvalidOperationException("Must not fetch library patch."), client, default);
        Assert.False(result.Approved);
        Assert.Contains(".gitignore", Assert.Single(result.Findings));
        Assert.Empty(client.Prompts);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReviewsEveryPageBeforeSynthesisAndDoesNotDiscardFindings(bool pageFinding)
    {
        var reference = Reference("src/game.ts", "dist/old.js") with { RemovedFiles = ["dist/old.js"] };
        var offsets = new List<int>();
        var client = new Client(number => number < 2
            ? JsonSerializer.Serialize(new {
                summary = "Reviewed source section.", findings = pageFinding && number == 0 ? new[] { "src/game.ts: fix invalid state" } : [] })
            : JsonSerializer.Serialize(new { approved = !pageFinding,
                summary = "Reviewed the complete change.", findings = pageFinding ? new[] { "src/game.ts: fix invalid state" } : [] }));
        var result = await BranchDiffTechnicalReview.ExecuteAsync(reference, new { requirements = "Works" }, (read, _) =>
        {
            offsets.Add(read.Offset);
            Assert.Equal(reference.PublicationId, read.PublicationId);
            return Task.FromResult(reference with { DiffPage = new(Digest, read.Offset, 6, read.Offset == 0 ? "abc" : "def", read.Offset == 0 ? 3 : null) });
        }, client, default);
        Assert.Equal(new[] { 0, 3 }, offsets);
        Assert.Equal(3, client.Prompts.Count);
        Assert.Equal(!pageFinding, result.Approved);
        Assert.Equal(Source, result.SourceCommitSha);
        Assert.Equal(Target, result.TargetCommitSha);
        var synthesis = JsonDocument.Parse(client.Prompts[^1].Split('\n').Last()).RootElement;
        var pages = synthesis.GetProperty("reviews").EnumerateArray().ToArray();
        Assert.Equal(new[] { 0, 3 }, pages.Select(x => x.GetProperty("offset").GetInt32()));
        Assert.All(pages, page =>
        {
            Assert.Equal(Source, page.GetProperty("sourceCommitSha").GetString());
            Assert.Equal(Target, page.GetProperty("targetCommitSha").GetString());
        });
        Assert.All(client.Prompts, prompt => Assert.True(prompt.Length < 60000));
    }

    [Theory]
    [InlineData("target")]
    [InlineData("source")]
    [InlineData("publication")]
    [InlineData("digest")]
    [InlineData("offset")]
    [InlineData("gap")]
    public async Task ChangedOrIncompletePagesCannotApprove(string change)
    {
        var reference = Reference("src/game.ts");
        var client = new Client(_ => throw new InvalidOperationException("Bad pages must never reach a model."));
        await Assert.ThrowsAsync<InvalidOperationException>(() => BranchDiffTechnicalReview.ExecuteAsync(reference, new { }, (_, _) =>
            Task.FromResult(reference with
            {
                PublicationId = change == "publication" ? Guid.NewGuid() : reference.PublicationId,
                CandidateCommitSha = change == "source" ? Target : Source,
                TargetCommitSha = change == "target" ? Source : Target,
                DiffPage = new(change == "digest" ? Source : Digest, change == "offset" ? 1 : 0, 6, "abc", change == "gap" ? 4 : 3)
            }), client, default));
    }

    [Fact]
    public async Task FinalApprovalCannotEraseAnUnresolvedPageFinding()
    {
        var reference = Reference("source.cs");
        var client = new Client(number => number == 0
            ? JsonSerializer.Serialize(new { summary = "Defect", findings = new[] { "Fix source.cs" } })
            : JsonSerializer.Serialize(new { approved = true, summary = "Fine", findings = Array.Empty<string>() }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => BranchDiffTechnicalReview.ExecuteAsync(reference, null,
            (_, _) => Task.FromResult(reference with { DiffPage = new(Digest, 0, 3, "abc", null) }), client, default));
    }

    [Fact]
    public async Task ModelCannotSwapTheAuthoritativeCommitPairOrPageOffset()
    {
        // The October 15 live response reversed source and target. Model-generated identity
        // fields are no longer part of the review contract or used to identify evidence.
        var reference = Reference("source.cs");
        var client = new Client(number => number == 0
            ? JsonSerializer.Serialize(new { sourceCommitSha = Target, targetCommitSha = Source, offset = 12000,
                summary = "Found defect", findings = new[] { "source.cs: fix invalid state" } })
            : JsonSerializer.Serialize(new { sourceCommitSha = Target, targetCommitSha = Source, approved = false,
                summary = "Fix the defect", findings = new[] { "source.cs: fix invalid state" } }));
        var result = await BranchDiffTechnicalReview.ExecuteAsync(reference, null,
            (_, _) => Task.FromResult(reference with { DiffPage = new(Digest, 0, 3, "abc", null) }), client, default);
        Assert.False(result.Approved);
        Assert.Equal(Source, result.SourceCommitSha);
        Assert.Equal(Target, result.TargetCommitSha);
        Assert.Equal("source.cs: fix invalid state", Assert.Single(result.Findings));
        var synthesis = JsonDocument.Parse(client.Prompts[^1].Split('\n').Last()).RootElement;
        var page = Assert.Single(synthesis.GetProperty("reviews").EnumerateArray());
        Assert.Equal(0, page.GetProperty("offset").GetInt32());
        Assert.Equal(Source, page.GetProperty("sourceCommitSha").GetString());
        Assert.Equal(Target, page.GetProperty("targetCommitSha").GetString());
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"summary\":\"\",\"findings\":[]}")]
    [InlineData("{\"summary\":\"Reviewed\",\"findings\":null}")]
    [InlineData("{\"summary\":\"Reviewed\",\"findings\":[\"\"]}")]
    public async Task MissingReviewContentCannotReachSynthesis(string response)
    {
        var reference = Reference("source.cs");
        var client = new Client(_ => response);
        await Assert.ThrowsAsync<InvalidOperationException>(() => BranchDiffTechnicalReview.ExecuteAsync(reference, null,
            (_, _) => Task.FromResult(reference with { DiffPage = new(Digest, 0, 3, "abc", null) }), client, default));
        Assert.Single(client.Prompts);
    }

    private sealed class Client(Func<int, string> response) : IChatClient
    {
        public List<string> Prompts { get; } = [];
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            var number = Prompts.Count;
            Prompts.Add(string.Join("\n", messages.Select(x => x.Text)));
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, response(number))));
        }
        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }
}
