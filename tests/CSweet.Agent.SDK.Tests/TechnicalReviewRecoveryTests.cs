using System.Text.Json;
using CSweet.WorkManagement.Contracts;
using Microsoft.Extensions.AI;

namespace CSweet.Agent.SDK.Tests;

public sealed class TechnicalReviewRecoveryTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static BranchDiffTechnicalReview.FinalContent Final(int summary = 1662, int evidence = 621) =>
        new(true, new string('s', summary), [], [], [new("Exact accepted criterion", true, new string('e', evidence), [0])]);
    private static string Encode(object value) => JsonSerializer.Serialize(value, Json);

    [Theory]
    [InlineData(4096)]
    [InlineData(16000)]
    public void FullAcceptedReviewSurvivesTheStageStatusStorageBoundary(int length)
    {
        var assignment = JsonSerializer.Deserialize<WorkExecutionAssignment>("{}")! with { StageExecutionId = Guid.NewGuid(), AttemptId = Guid.NewGuid() };
        var decision = new BranchDiffTechnicalReview.Decision(new string('a', 40), new string('b', 40), true, new string('s', length), []);
        var outcome = DeliveryTaskTechnicalReview.Outcome(assignment, decision);
        Assert.True(outcome.Summary.Length <= 4096);
        Assert.Equal(decision.Summary, outcome.Output.GetProperty("summary").GetString());
        Assert.Equal("approved", outcome.OutcomeCode);
        if (length > 4096) Assert.Contains("Full review summary is retained", outcome.Summary);
    }

    [Fact]
    public async Task PreviouslyBlockedReportFitsAndSchemaCarriesTheSameBounds()
    {
        var client = new Client((_, _) => Encode(Final()));
        var result = await Read(client);
        Assert.True(result.Approved);
        Assert.Equal(621, Assert.Single(result.Criteria).Evidence.Length);
        var format = Assert.IsType<ChatResponseFormatJson>(Assert.Single(client.Options).ResponseFormat);
        var schema = format.Schema!.Value;
        Assert.Equal(JsonValueKind.String, schema.GetProperty("type").ValueKind);
        Assert.Equal("object", schema.GetProperty("type").GetString());
        Assert.Equal(16000, schema.GetProperty("properties").GetProperty("summary").GetProperty("maxLength").GetInt32());
        Assert.Contains("\"maxItems\":16", schema.GetRawText());
        Assert.Equal(32768, client.Options[0].MaxOutputTokens);
    }

    private sealed record NullableReviewNote(string? Note);

    [Fact]
    public async Task Object_root_does_not_remove_nested_nullability_or_accept_a_null_review()
    {
        var client = new Client((_, _) => "{\"note\":null}");
        var value = await TechnicalReviewJson.ReadAsync<NullableReviewNote>(client, [new(ChatRole.User, "Review evidence")],
            "nullable note", _ => { }, default);
        Assert.Null(value.Note);
        var schema = Assert.IsType<ChatResponseFormatJson>(client.Options[0].ResponseFormat).Schema!.Value;
        Assert.Equal("object", schema.GetProperty("type").GetString());
        Assert.Contains("null", schema.GetProperty("properties").GetProperty("note").GetRawText());
        var invalid = new Client((_, _) => "null");
        await Assert.ThrowsAsync<InvalidOperationException>(() => TechnicalReviewJson.ReadAsync<NullableReviewNote>(invalid,
            [new(ChatRole.User, "Review evidence")], "null review", _ => { }, default));
        Assert.Equal(4, invalid.Calls);
    }

    [Fact]
    public async Task AllOversizeFieldsAreReportedTogetherAndMultipleCorrectionsAreBounded()
    {
        var client = new Client((call, messages) =>
        {
            if (call == 1) return Encode(Final(16001, 16002));
            Assert.Equal(4, messages.Count);
            if (call == 2)
            {
                var feedback = messages.Last().Text!;
                Assert.Contains("$.summary: length 16001", feedback);
                Assert.Contains("$.criteria[0].evidence: length 16002", feedback);
                return "{\"summary\":\"still malformed\"}";
            }
            Assert.DoesNotContain("16001", messages.Last().Text!);
            return Encode(Final());
        });
        Assert.True((await Read(client)).Approved);
        Assert.Equal(3, client.Calls);
    }

    [Fact]
    public async Task ExhaustionIsAnExplicitPlatformIssueAndDoesNotReturnApproval()
    {
        var client = new Client((_, _) => Encode(Final(16001, 16002)));
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Read(client));
        Assert.Equal(4, client.Calls);
        Assert.Contains("after 3 corrections", error.Message);
        Assert.Contains("$.summary", error.Message);
        Assert.Contains("$.criteria[0].evidence", error.Message);
        Assert.Contains("Escalate this review-processing issue to management", error.Message);
    }

    [Fact]
    public async Task LargeValidResponseIsPreservedAndOverallBudgetIsStillEnforced()
    {
        var large = Final(16000, 16000) with
        {
            Criteria = Enumerable.Range(0, 5).Select(i => new BranchDiffTechnicalReview.Criterion("Criterion " + i, true, new string('e', 16000), [0])).ToArray()
        };
        var client = new Client((_, _) => Encode(large));
        Assert.Equal(5, (await Read(client)).Criteria.Count);
        Assert.True(Encode(large).Length > 96000);
        var oversized = large with { Criteria = Enumerable.Repeat(large.Criteria[0], 8).ToArray() };
        var rejected = new Client((_, _) => Encode(oversized));
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Read(rejected));
        Assert.Contains("exceeds 128000 characters", error.Message);
        Assert.Equal(4, rejected.Calls);
    }

    [Fact]
    public async Task ResumeReusesEverySavedPageAndOnlyCorrectsFinalSynthesis()
    {
        const int total = 42285;
        var reference = new GitMergeReview(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Game", new string('a', 40), null, "", [], [], "AwaitingValidation")
        {
            TargetCommitSha = new string('b', 40), TargetBranch = "story", PatchDigest = new string('c', 64), ChangedFiles = ["src/audio/AudioManager.ts"]
        };
        var reads = new List<int>();
        var loads = new List<int>();
        var client = new Client((call, messages) =>
        {
            Assert.DoesNotContain("\"page\":", messages[1].Text!);
            return Encode(call == 1 ? Final(16001, 16002) : Final());
        });
        var result = await BranchDiffTechnicalReview.ExecuteAsync(reference,
            new WorkItemPlanningSpecification(["Requirements"], ["Exact accepted criterion"], []),
            (read, _) =>
            {
                reads.Add(read.Offset);
                var count = Math.Min(12000, total - read.Offset);
                return Task.FromResult(reference with { DiffPage = new(reference.PatchDigest, read.Offset, total, new string('x', count), read.Offset + count < total ? read.Offset + count : null) });
            }, client, default,
            (offset, _) =>
            {
                loads.Add(offset);
                var count = Math.Min(12000, total - offset);
                return Task.FromResult<BranchDiffTechnicalReview.PageReview?>(new(offset, count, total, BranchDiffTechnicalReview.Hash(new string('x', count)),
                    new("Previously accepted source review", [], [], [])));
            }, (_, _) => throw new Exception("Saved pages must not be rewritten."));
        Assert.True(result.Approved);
        Assert.Equal(total, result.CoveredCharacters);
        Assert.Equal(new[] { 0, 12000, 24000, 36000 }, reads);
        Assert.Equal(reads, loads);
        Assert.Equal(2, client.Calls);
    }

    private static Task<BranchDiffTechnicalReview.FinalContent> Read(Client client) =>
        TechnicalReviewJson.ReadAsync<BranchDiffTechnicalReview.FinalContent>(client,
            [new(ChatRole.System, "Review the authorized evidence."), new(ChatRole.User, "Evidence")], "final synthesis", _ => { }, default);

    private sealed class Client(Func<int, IReadOnlyList<ChatMessage>, string> response) : IChatClient
    {
        public int Calls { get; private set; }
        public List<ChatOptions> Options { get; } = [];
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            Calls++;
            Options.Add(options!);
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, response(Calls, messages.ToArray()))) { FinishReason = ChatFinishReason.Stop });
        }
        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public object? GetService(Type type, object? key = null) => null;
        public void Dispose() { }
    }
}
