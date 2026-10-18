using System.Text.Json;
using CSweet.WorkManagement.Contracts;
using Microsoft.Extensions.AI;

namespace CSweet.Agent.SDK.Tests;

public sealed class AggregateReviewChunksTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly string Digest = new('a', 64);
    private static readonly string[] Criteria = ["Locked dependencies", "Procedural graphics", "Audio interfaces", "Deferred QA remains required", "Internal promotion only"];

    [Fact]
    public async Task RestartResumesAcceptedChunksAndCorrectsOnlyTheCurrentSmallAssessment()
    {
        var ledger = new Dictionary<int, AggregateReviewChunks.Checkpoint>();
        using var interrupted = new Client((call, messages) => call == 1 ? Report(Criteria[..2]) : throw new OperationCanceledException());
        await Assert.ThrowsAsync<OperationCanceledException>(() => Run(interrupted, ledger));
        Assert.Equal(2, interrupted.Calls); Assert.Single(ledger);
        var progress = new List<(int, int, bool)>();
        using var resumed = new Client((call, messages) => {
            if (call == 1) { AssertSelected(messages, Criteria[2..4]); return "{invalid"; }
            if (call == 2) { Assert.Contains("Correction 1/3", messages.Last().Text); AssertSelected(messages, Criteria[2..4]); return Report(Criteria[2..4]); }
            if (call == 3) { AssertSelected(messages, Criteria[4..]); return Report(Criteria[4..]); }
            return Consistency();
        });
        var result = await Run(resumed, ledger, progress: (i,n,r,_) => { progress.Add((i,n,r)); return Task.CompletedTask; });
        Assert.True(result.Approved); Assert.Equal(Criteria, result.Criteria.Select(x => x.Criterion));
        Assert.Equal(3, ledger.Count); Assert.Equal(4, resumed.Calls);
        Assert.Contains((0,3,true), progress); Assert.Contains((3,3,false), progress);
    }

    [Theory]
    [InlineData(true, false)] [InlineData(false, true)]
    public async Task IndividualAndCrossCandidateFailuresCannotBeOverriddenByOtherPassingAssessments(bool criterionFailure, bool consistencyFailure)
    {
        var ledger = new Dictionary<int, AggregateReviewChunks.Checkpoint>();
        using var client = new Client((call, _) => call <= 3
            ? Report(Criteria.Skip((call-1)*2).Take(2).ToArray(), fail: criterionFailure && call == 1)
            : Consistency(consistencyFailure));
        var result = await Run(client, ledger);
        Assert.False(result.Approved);
        if (criterionFailure) { Assert.Contains("Real criterion defect", result.Findings); Assert.Contains(result.Criteria, c => !c.Satisfied); }
        if (consistencyFailure) Assert.Contains("Cross-repository incompatibility", result.Findings);
        Assert.Equal(Criteria.Length, result.Criteria.Count);
    }

    [Fact]
    public async Task RevokedAuthorityAfterGenerationCannotSaveAssessmentOrReturnVerdict()
    {
        var ledger = new Dictionary<int, AggregateReviewChunks.Checkpoint>(); var valid = true;
        using var client = new Client((_, _) => { valid = false; return Report(Criteria[..2]); });
        var error = await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Run(client, ledger,
            authorize: _ => valid ? Task.CompletedTask : throw new UnauthorizedAccessException("Current assignment revoked")));
        Assert.Contains("revoked", error.Message); Assert.Empty(ledger); Assert.Equal(1, client.Calls);
    }

    [Theory]
    [InlineData("identity")] [InlineData("index")] [InlineData("criteria")] [InlineData("verdict")]
    public async Task IncompatibleOrInvalidRetainedAssessmentNeverBecomesApproval(string fault)
    {
        var report = JsonSerializer.Deserialize<WorkDeliveryReviewResult>(Report(Criteria[..2]), Json)!;
        var checkpoint = new AggregateReviewChunks.Checkpoint("identity",0,report);
        checkpoint = fault switch {
            "identity" => checkpoint with { Identity = "other-candidate-or-planning" },
            "index" => checkpoint with { Index = 1 },
            "criteria" => checkpoint with { Report = report with { Criteria = [] } },
            _ => checkpoint with { Report = report with { Findings = ["Retained defect"] } }
        };
        var ledger = new Dictionary<int, AggregateReviewChunks.Checkpoint> { [0] = checkpoint };
        using var client = new Client((_, _) => throw new InvalidOperationException("Must not reach model"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Run(client, ledger));
        Assert.Equal(0, client.Calls); Assert.Single(ledger);
    }

    private static void AssertSelected(IReadOnlyList<ChatMessage> messages, string[] expected)
    {
        using var parsed = JsonDocument.Parse(messages[1].Text);
        Assert.Equal(expected, parsed.RootElement.GetProperty("acceptanceCriteria").EnumerateArray().Select(x => x.GetString()));
    }
    private static string Report(string[] criteria, bool fail = false) => JsonSerializer.Serialize(new WorkDeliveryReviewResult(Digest,
        !fail,"Concise evidence-based assessment",criteria.Select(c => new WorkDeliveryCriterionResult(c,!fail,"Exact candidate evidence")).ToArray(),
        fail ? ["Real criterion defect"] : []),Json);
    private static string Consistency(bool fail = false) => JsonSerializer.Serialize(new AggregateReviewChunks.Consistency("Whole-candidate consistency assessed",
        fail ? ["Cross-repository incompatibility"] : []),Json);
    private static Task<WorkDeliveryReviewResult> Run(Client client, Dictionary<int, AggregateReviewChunks.Checkpoint> ledger,
        Func<CancellationToken,Task>? authorize = null, Func<int,int,bool,CancellationToken,Task>? progress = null) =>
        AggregateReviewChunks.ExecuteAsync(Digest,Criteria,new { requirements = "All original requirements", source = "Exact full candidate" },client,default,"identity",
            (i,_) => Task.FromResult(ledger.GetValueOrDefault(i)), (p,_) => { ledger.Add(p.Index,p); return Task.CompletedTask; },
            authorize ?? (_ => Task.CompletedTask),progress ?? ((_,_,_,_) => Task.CompletedTask));

    private sealed class Client(Func<int,IReadOnlyList<ChatMessage>,string> respond) : IChatClient
    {
        internal int Calls;
        public void Dispose() { }
        public object? GetService(Type type, object? key = null) => null;
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested(); Assert.NotNull(options!.ResponseFormat); Assert.Equal(128000,options.MaxOutputTokens);
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant,respond(++Calls,messages.ToArray()))));
        }
        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
