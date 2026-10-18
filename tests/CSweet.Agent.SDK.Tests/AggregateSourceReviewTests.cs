using System.Text.Json;
using CSweet.WorkManagement.Contracts;
using Microsoft.Extensions.AI;

namespace CSweet.Agent.SDK.Tests;

public sealed class AggregateSourceReviewTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public void LargeUnicodeFilesAreCoveredExactlyOnceWithoutSplittingSurrogates()
    {
        var text = new string('a', 47999) + "🎮" + new string('b', 90000);
        DeliveryReviewSourceFile[] files = [new("source/game.js", "hash", text.Length, text),
            new("assets/sound.bin", "binary", 20, null), new("empty.txt", "empty", 0, "")];
        var groups = AggregateSourceReview.Partition(files);
        Assert.True(groups.Count > 1);
        var parts = groups.SelectMany(g => g).Where(p => p.Path == "source/game.js").ToArray();
        Assert.Equal(text, string.Concat(parts.Select(p => p.Content)));
        var offset = 0;
        foreach (var part in parts)
        {
            Assert.Equal(offset, part.Offset);
            Assert.False(char.IsLowSurrogate(part.Content![0]));
            Assert.False(char.IsHighSurrogate(part.Content[^1]));
            offset += part.Content.Length;
        }
        Assert.Single(groups.SelectMany(g => g), p => p.Path == "assets/sound.bin" && p.Content is null);
        Assert.Single(groups.SelectMany(g => g), p => p.Path == "empty.txt" && p.Content == "");
        Assert.All(groups, g => Assert.InRange(g.Sum(p => (p.Content?.Length ?? 0) + p.Path.Length + 256), 1, 60000));
    }

    [Fact]
    public async Task InterruptedInspectionResumesAcceptedPartsAndRetainsDefects()
    {
        var ledger = new Dictionary<string, AgentOperatingStateResponse>();
        var runtime = Runtime(ledger);
        var assignment = JsonSerializer.Deserialize<WorkExecutionAssignment>("{}")! with {
            ExecutionId = Guid.NewGuid(), StageExecutionId = Guid.NewGuid(), DeliveryPlanId = Guid.NewGuid(),
            ScopeRevision = 3, Candidate = new(new string('a', 64), 3, [], []) };
        DeliveryReviewSourceFile[] source = [new("game.js", "hash", 150000, new string('a', 150000))];
        using var first = new Client((call, messages) => call == 1 ? Report(messages, ["Real implementation defect"]) : throw new OperationCanceledException());
        await Assert.ThrowsAsync<OperationCanceledException>(() => Run(first));
        Assert.Single(ledger);
        using var second = new Client((_, messages) => Report(messages, []));
        var reports = await Run(second);
        Assert.Equal(3, reports.Count);
        Assert.Equal(2, second.Calls);
        Assert.Contains("Real implementation defect", reports[0].Findings);
        Assert.Equal(3, ledger.Count);

        Task<IReadOnlyList<AggregateSourceReview.Report>> Run(Client client) => AggregateSourceReview.ExecuteAsync(
            assignment, new { requirements = "Scaffold" }, source, runtime.CreateContext(), client, _ => Task.CompletedTask, default);
    }

    [Fact]
    public async Task RevokedAssignmentCannotPersistGeneratedInspection()
    {
        var ledger = new Dictionary<string, AgentOperatingStateResponse>();
        var runtime = Runtime(ledger); var authorized = true;
        var assignment = JsonSerializer.Deserialize<WorkExecutionAssignment>("{}")! with {
            ExecutionId = Guid.NewGuid(), StageExecutionId = Guid.NewGuid(), Candidate = new(new string('a', 64), 3, [], []) };
        using var client = new Client((_, messages) => { authorized = false; return Report(messages, []); });
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => AggregateSourceReview.ExecuteAsync(
            assignment, new { requirements = "Scaffold" }, [new("game.js", "hash", 3, "abc")], runtime.CreateContext(), client,
            _ => authorized ? Task.CompletedTask : throw new UnauthorizedAccessException(), default));
        Assert.Empty(ledger);
    }

    [Theory]
    [InlineData("missing")] [InlineData("duplicate")] [InlineData("foreign")]
    public void IncompleteOrSubstitutedSourceCoverageIsRejected(string fault)
    {
        AggregateSourceReview.Part[] parts = [new("game.js", "hash", 3, 0, "abc")];
        string[] coverage = fault switch { "missing" => [], "duplicate" => ["game.js:0", "game.js:0"], _ => ["other.js:0"] };
        Assert.Throws<InvalidOperationException>(() => AggregateSourceReview.Validate(new("Inspected", [], coverage), parts));
    }

    private static AgentTestRuntime Runtime(Dictionary<string, AgentOperatingStateResponse> ledger) => new AgentTestRuntime()
        .RegisterCapability<AgentOperatingStateReadRequest, AgentOperatingStateReadResponse>(PlatformCapabilities.AgentOperatingStateRead,
            (r, _) => Task.FromResult(new AgentOperatingStateReadResponse(ledger.GetValueOrDefault(r.StateKey))))
        .RegisterCapability<AgentOperatingStateWriteRequest, AgentOperatingStateResponse>(PlatformCapabilities.AgentOperatingStateWrite, (r, _) => {
            var now = DateTimeOffset.UtcNow;
            var stored = new AgentOperatingStateResponse(Guid.NewGuid(), r.StateKey, r.SchemaId, r.SchemaVersion, r.Status,
                r.SourceRevisions, r.ConditionCodes, r.DecisionFingerprint, r.OpenCommitmentCorrelations, r.AttentionReviewId, r.Payload, 1, now, now);
            ledger.Add(r.StateKey, stored); return Task.FromResult(stored);
        });

    private static string Report(IReadOnlyList<ChatMessage> messages, string[] findings)
    {
        Assert.True(messages.Sum(m => m.Text.Length) < 65000);
        using var input = JsonDocument.Parse(messages[1].Text);
        return JsonSerializer.Serialize(new AggregateSourceReview.Report("Concrete source evidence", findings,
            input.RootElement.GetProperty("parts").EnumerateArray().Select(p => p.GetProperty("key").GetString()!).ToArray()), Json);
    }

    private sealed class Client(Func<int, IReadOnlyList<ChatMessage>, string> respond) : IChatClient
    {
        internal int Calls;
        public void Dispose() { }
        public object? GetService(Type type, object? key = null) => null;
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            Assert.Equal(128000, options!.MaxOutputTokens);
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, respond(++Calls, messages.ToArray()))));
        }
        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
