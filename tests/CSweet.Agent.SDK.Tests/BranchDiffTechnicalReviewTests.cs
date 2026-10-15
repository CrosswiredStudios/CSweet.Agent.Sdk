using System.Text.Json;
using CSweet.WorkManagement.Contracts;
using Microsoft.Extensions.AI;

namespace CSweet.Agent.SDK.Tests;

public sealed class BranchDiffTechnicalReviewTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly string Source = new('a', 40), Target = new('b', 40), Digest = new('c', 64);
    private static readonly BranchDiffTechnicalReview.Issue Defect = new("server.js", "Malformed URLs crash the server", "decodeURIComponent has no exception handler", "Return 400 for malformed escapes");
    private static GitMergeReview Reference() => new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Game", Source, null, "", [], [], "AwaitingValidation")
        { TargetCommitSha = Target, TargetBranch = "story", PatchDigest = Digest, ChangedFiles = ["server.js", "package-lock.json"] };
    private static BranchDiffTechnicalReview.PageContent Page(bool defect = false) => new("Reviewed source section", defect ? [Defect] : [], ["Nonblocking observation"], ["Check implementation on the next page"]);
    private static string Serialize(object value) => JsonSerializer.Serialize(value, Json);
    private static GitMergeReview Read(GitMergeReview reference, GitReviewDiffRead read, int total) => reference with
        { DiffPage = new(Digest, read.Offset, total, new string('x', Math.Min(12000, total - read.Offset)), read.Offset + 12000 < total ? read.Offset + 12000 : null) };
    private static string Final(JsonElement input, bool resolve = true)
    {
        var issues = input.GetProperty("findings").EnumerateArray().ToArray();
        var criteria = input.GetProperty("reference").GetProperty("planning");
        var names = criteria.TryGetProperty("acceptanceCriteria", out var entries) ? entries.EnumerateArray().Select(x => x.GetString()!).ToArray() : [];
        return Serialize(new BranchDiffTechnicalReview.FinalContent(resolve || issues.Length == 0, "Complete independent review", [],
            issues.Select(i => new BranchDiffTechnicalReview.Resolution(i.GetProperty("findingId").GetString()!, resolve ? "resolved" : "unresolved", "Source evidence checked against the relevant implementation", [0])).ToArray(),
            names.Select(c => new BranchDiffTechnicalReview.Criterion(c, true, "Observed source and published build evidence", [0])).ToArray()));
    }

    [Fact]
    public async Task ThirteenPagesCorrectMalformedFourthResponseWithoutRepeatingEarlierPages()
    {
        var reference = Reference();
        var retained = new Dictionary<int, BranchDiffTechnicalReview.PageReview>();
        var reads = new List<int>();
        var client = new Client((messages, input) => input.TryGetProperty("page", out var page)
            ? Serialize(Page()) + (page.GetProperty("offset").GetInt32() == 36000 && messages.Count == 2 ? "}" : "")
            : Final(input));
        var result = await BranchDiffTechnicalReview.ExecuteAsync(reference, new { }, (read, _) =>
        { reads.Add(read.Offset); return Task.FromResult(Read(reference, read, 146391)); }, client, default,
            (offset, _) => Task.FromResult(retained.GetValueOrDefault(offset)),
            (page, _) => { retained.Add(page.Offset, page); return Task.CompletedTask; });
        Assert.True(result.Approved); Assert.Equal(13, result.PageCount); Assert.Equal(146391, result.CoveredCharacters);
        Assert.Equal(13, reads.Count); Assert.Equal(13, retained.Count); Assert.Equal(15, client.Calls);
        Assert.All(client.Formats, format => Assert.IsType<ChatResponseFormatJson>(format));
        Assert.Equal(Source, result.SourceCommitSha); Assert.Equal(Target, result.TargetCommitSha);
    }

    [Fact]
    public async Task InterruptedReviewResumesPersistedPagesButReauthorizesEveryPage()
    {
        var reference = Reference(); var retained = new Dictionary<int, BranchDiffTechnicalReview.PageReview>();
        var reads = new List<int>();
        Task<GitMergeReview> ReadPage(GitReviewDiffRead read, CancellationToken _)
        { reads.Add(read.Offset); return Task.FromResult(Read(reference, read, 48001)); }
        Task<BranchDiffTechnicalReview.PageReview?> Load(int offset, CancellationToken _) => Task.FromResult(retained.GetValueOrDefault(offset));
        Task Save(BranchDiffTechnicalReview.PageReview page, CancellationToken _) { retained[page.Offset] = page; return Task.CompletedTask; }
        var broken = new Client((messages, input) => input.GetProperty("page").GetProperty("offset").GetInt32() == 36000 ? "{}" : Serialize(Page()));
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => BranchDiffTechnicalReview.ExecuteAsync(reference, new { }, ReadPage, broken, default, Load, Save));
        Assert.Contains("offset 36000", error.Message); Assert.Contains("one correction", error.Message); Assert.Equal(3, retained.Count);
        var resumed = new Client((messages, input) => input.TryGetProperty("page", out _) ? Serialize(Page()) : Final(input));
        var result = await BranchDiffTechnicalReview.ExecuteAsync(reference, new { }, ReadPage, resumed, default, Load, Save);
        Assert.True(result.Approved); Assert.Equal(3, resumed.Calls); Assert.Equal(new[] { 0,12000,24000,36000,0,12000,24000,36000,48000 }, reads);
    }

    [Theory]
    [InlineData("source")][InlineData("target")][InlineData("publication")][InlineData("digest")][InlineData("offset")][InlineData("gap")]
    public async Task ChangedOrIncompletePagesNeverReachInference(string change)
    {
        var reference = Reference(); var client = new Client((_, _) => throw new Exception("No model"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => BranchDiffTechnicalReview.ExecuteAsync(reference, new { }, (read, _) =>
            Task.FromResult(reference with { CandidateCommitSha = change == "source" ? Target : Source,
                TargetCommitSha = change == "target" ? Source : Target, PublicationId = change == "publication" ? Guid.NewGuid() : reference.PublicationId,
                DiffPage = new(change == "digest" ? Source : Digest, change == "offset" ? 1 : 0, 6, "abc", change == "gap" ? 4 : 3) }), client, default));
        Assert.Equal(0, client.Calls);
    }

    [Fact]
    public async Task FinalCannotSilentlyEraseABlockingFinding()
    {
        var reference = Reference();
        var client = new Client((messages, input) => input.TryGetProperty("page", out _) ? Serialize(Page(true)) :
            Serialize(new BranchDiffTechnicalReview.FinalContent(true, "Ignore issue", [], [], [])));
        await Assert.ThrowsAsync<InvalidOperationException>(() => BranchDiffTechnicalReview.ExecuteAsync(reference, new { },
            (read, _) => Task.FromResult(Read(reference, read, 3)), client, default));
        Assert.Equal(3, client.Calls);
    }

    [Theory]
    [InlineData(true)][InlineData(false)]
    public async Task ExplicitEvidenceResolvesConcernOrReturnsItForRework(bool resolve)
    {
        var reference = Reference();
        var client = new Client((messages, input) => input.TryGetProperty("page", out _) ? Serialize(Page(true)) : Final(input, resolve));
        var result = await BranchDiffTechnicalReview.ExecuteAsync(reference, new { }, (read, _) => Task.FromResult(Read(reference, read, 3)), client, default);
        Assert.Equal(resolve, result.Approved); Assert.Single(result.Resolutions);
        if (resolve) Assert.Empty(result.Findings); else Assert.Contains("Malformed URLs", Assert.Single(result.Findings));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"summary\":\"Reviewed\",\"findings\":[]}")]
    [InlineData("{\"summary\":\"Reviewed\",\"blockingFindings\":[],\"observations\":[],\"questions\":[],\"sourceCommitSha\":\"forged\"}")]
    [InlineData("{\"summary\":\"a\",\"summary\":\"b\",\"blockingFindings\":[],\"observations\":[],\"questions\":[]}")]
    public async Task OldMalformedOrAmbiguousContentReceivesExactlyOneCorrection(string bad)
    {
        var reference = Reference(); var client = new Client((_, _) => bad);
        await Assert.ThrowsAsync<InvalidOperationException>(() => BranchDiffTechnicalReview.ExecuteAsync(reference, new { },
            (read, _) => Task.FromResult(Read(reference, read, 3)), client, default));
        Assert.Equal(2, client.Calls);
    }

    [Fact]
    public async Task RetainedContentWithAChangedPageHashCannotApprove()
    {
        var reference = Reference(); var client = new Client((_, _) => throw new Exception("No model"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => BranchDiffTechnicalReview.ExecuteAsync(reference, new { },
            (read, _) => Task.FromResult(Read(reference, read, 3)), client, default,
            (_, _) => Task.FromResult<BranchDiffTechnicalReview.PageReview?>(new(0,3,3,"wrong",Page()))));
    }

    [Fact]
    public async Task OversizeSynthesisReturnsReworkWithoutDroppingCoverage()
    {
        var reference = Reference(); var client = new Client((messages, input) =>
        {
            Assert.True(input.TryGetProperty("page", out _));
            return Serialize(new BranchDiffTechnicalReview.PageContent(new string('s',350), [], [new string('o',200), new string('p',200)], [new string('q',200),new string('r',200)]));
        });
        var result = await BranchDiffTechnicalReview.ExecuteAsync(reference, new { }, (read, _) => Task.FromResult(Read(reference, read, 480000)), client, default);
        Assert.False(result.Approved); Assert.Equal(40, result.PageCount); Assert.Equal(480000,result.CoveredCharacters); Assert.NotEmpty(result.Findings);
    }

    [Fact]
    public async Task GeneratedAdditionsReturnActionableReworkWithoutInference()
    {
        var client = new Client((_, _) => throw new Exception("No model"));
        var result = await BranchDiffTechnicalReview.ExecuteAsync(Reference() with { ChangedFiles=["dist/game.js"] }, new { },
            (_,_) => throw new Exception("No read"),client,default);
        Assert.False(result.Approved); Assert.Contains(".gitignore",Assert.Single(result.Findings));
    }

    internal sealed class Client(Func<IReadOnlyList<ChatMessage>,JsonElement,string> response) : IChatClient
    {
        public int Calls { get; private set; }
        public List<ChatResponseFormat?> Formats { get; } = [];
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages,ChatOptions? options=null,CancellationToken cancellationToken=default)
        {
            Calls++; Formats.Add(options?.ResponseFormat); var list=messages.ToArray();
            using var input=JsonDocument.Parse(list[1].Text);
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant,response(list,input.RootElement))) { FinishReason=ChatFinishReason.Stop });
        }
        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,ChatOptions? options=null,CancellationToken cancellationToken=default)=>throw new NotSupportedException();
        public object? GetService(Type type,object? key=null)=>null;
        public void Dispose() { }
    }
}