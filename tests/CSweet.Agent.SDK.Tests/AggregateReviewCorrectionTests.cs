using System.Text.Json;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using CSweet.WorkManagement.Contracts;
using Microsoft.Extensions.AI;

namespace CSweet.Agent.SDK.Tests;

public sealed class AggregateReviewCorrectionTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Theory]
    [InlineData("criterion")] [InlineData("digest")] [InlineData("json")]
    [InlineData("null")] [InlineData("contradiction")]
    public async Task CorrectsResponseWithExactIdentityAndCriteriaWhilePreservingRealRejection(string fault)
    {
        var f = new Fixture(); var valid = f.Report(false);
        var invalid = fault switch {
            "criterion" => Encode(valid with { Criteria = [new("Extra checklist", false, "Actual defect")] }),
            "digest" => Encode(valid with { CandidateDigest = new string('b', 64) }),
            "json" => "{invalid JSON", "null" => Encode(valid with { Criteria = null! }),
            _ => Encode(valid with { Approved = true }) };
        using var client = new Client((calls, messages) => {
            if (calls == 1) return invalid;
            Assert.Contains("Correction 1/3", messages.Last().Text);
            Assert.Contains("do not alter a verdict or suppress a defect", messages.Last().Text);
            if (fault == "criterion") Assert.Contains(f.Criterion, messages.Last().Text);
            if (fault == "digest") Assert.Contains(f.Candidate.Digest, messages.Last().Text);
            return Encode(valid);
        });
        var result = await f.Run(client);
        Assert.Equal(2, client.Calls); Assert.Equal(2, f.EvidenceReads);
        Assert.Equal("changes_requested", result.OutcomeCode);
        var report = result.Output.Deserialize<WorkDeliveryReviewResult>(Json)!;
        Assert.False(report.Approved); Assert.Equal(valid.Findings, report.Findings);
        Assert.Equal(f.Criterion, Assert.Single(report.Criteria).Criterion);
    }

    [Fact]
    public async Task ExhaustionIsAnAggregateProcessingFailureAndNeverAnAcceptedVerdict()
    {
        var f = new Fixture(); using var client = new Client((_, _) => Encode(f.Report(true) with { Criteria = [] }));
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => f.Run(client));
        Assert.Equal(4, client.Calls); Assert.Equal(1, f.EvidenceReads);
        Assert.Contains("C-Sweet aggregate review response invalid", error.Message);
        Assert.Contains("after 3 corrections", error.Message); Assert.Contains("No aggregate review verdict was accepted", error.Message);
        Assert.Contains(f.Criterion, error.Message);
    }

    [Theory]
    [InlineData("candidate")] [InlineData("principal")] [InlineData("planning")]
    public async Task ReauthorizesCurrentScopeAndPrincipalAfterModelResponse(string change)
    {
        var f = new Fixture(); using var client = new Client((_, _) => {
            f.Plan = change switch {
                "candidate" => f.Plan with { Executions = [f.Execution with { Candidate = f.Candidate with { Digest = new string('c', 64) } }] },
                "principal" => f.Plan with { Executions = [f.Execution with { Stages = [f.Stage with { AgentInstallationId = Guid.NewGuid() }] }] },
                _ => f.Plan with { Scopes = [f.Scope with { PlanningRevision = 2 }] } };
            return Encode(f.Report(true));
        });
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => f.Run(client));
        Assert.Contains("no verdict was accepted", error.Message);
    }

    [Fact]
    public async Task AggregateCallbackPersistsAuthorizedChunksAndResumesAcrossInterruptedAttempts()
    {
        var f = new Fixture(); var criteria = new[] { f.Criterion, "Check procedural source", "Retain final QA" };
        f.Scope = f.Scope with { Planning = f.Scope.Planning with { AcceptanceCriteria = criteria } };
        f.Plan = f.Plan with { Scopes = [f.Scope] };
        string Report(IEnumerable<string> selected) => Encode(new(f.Candidate.Digest,true,"Small assessment",
            selected.Select(c => new WorkDeliveryCriterionResult(c,true,"Concrete evidence")).ToArray(),[]));
        using var interrupted = new Client((call,_) => call == 1 ? Report(criteria.Take(2)) : throw new OperationCanceledException());
        await Assert.ThrowsAsync<OperationCanceledException>(() => f.Run(interrupted));
        Assert.Single(f.Ledger);
        using var resumed = new Client((call,_) => call == 1 ? Report(criteria.Skip(2)) : "{\"summary\":\"Whole-candidate consistency checked\",\"findings\":[]}");
        var result = await f.Run(resumed);
        Assert.Equal(2,resumed.Calls); Assert.Equal(2,f.Ledger.Count); Assert.Equal("approved",result.OutcomeCode);
        Assert.Equal(criteria,result.Output.Deserialize<WorkDeliveryReviewResult>(Json)!.Criteria.Select(x=>x.Criterion));
        Assert.Contains(f.Runtime.Progress,p=>p.GetProperty("resumed").GetBoolean());
    }

    [Fact]
    public async Task ConflictingDurableAggregateJudgmentCannotBeSilentlyReplacedByAnIdempotentWrite()
    {
        var f = new Fixture { AlterWrite = true }; var criteria = new[] { f.Criterion, "Second", "Third" };
        f.Scope = f.Scope with { Planning = f.Scope.Planning with { AcceptanceCriteria = criteria } }; f.Plan = f.Plan with { Scopes = [f.Scope] };
        using var client = new Client((_,_) => Encode(new(f.Candidate.Digest,true,"Original judgment",
            criteria.Take(2).Select(c=>new WorkDeliveryCriterionResult(c,true,"Concrete evidence")).ToArray(),[])));
        var error = await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Run(client));
        Assert.Contains("accepted ledger",error.Message); Assert.Equal(1,client.Calls);
    }

    private static string Encode(WorkDeliveryReviewResult report) => JsonSerializer.Serialize(report, Json);

    [Fact]
    public async Task LargeCandidateUsesBoundedInspectionWithoutRepeatingDocumentsOrRetryHistoryAndCannotEraseSourceFindings()
    {
        var f = new Fixture();
        var document = "UNIQUE_DOCUMENT_BODY " + new string('d', 40000);
        var documentHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(document)));
        var artifact = Guid.NewGuid(); var revision = Guid.NewGuid();
        var repository = JsonSerializer.Deserialize<WorkDeliveryRepositoryCandidate>("{}")! with {
            RepositoryId = Guid.NewGuid(), CandidateCommitSha = new string('c', 40) };
        f.Candidate = f.Candidate with { Repositories = [repository], Documents = [new(Guid.NewGuid(), artifact, revision, documentHash)] };
        f.Documents = [new(artifact, revision, documentHash, document)];
        using (var bytes = new MemoryStream())
        {
            using (var zip = new ZipArchive(bytes, ZipArchiveMode.Create, true))
            using (var writer = new StreamWriter(zip.CreateEntry("src/game.js").Open()))
                writer.Write(new string('x', 200000));
            f.Archive = bytes.ToArray();
        }
        var stages = Enumerable.Range(0, 94).Select(i => f.Stage with {
            Id = Guid.NewGuid(), StageKey = "build-readiness", Traversal = i, Status = "Completed" }).ToList();
        f.PriorOutcomes = stages.Select(s => new WorkExecutionOutcomeV1(s.Id, Guid.NewGuid(), WorkExecutionDispositions.Completed,
            "awaiting-compute", "STALE_STATUS " + new string('h', 3000), JsonSerializer.SerializeToElement(new {}), [], [])).ToList();
        var readiness = f.Stage with { Id = Guid.NewGuid(), StageKey = "build-readiness", Traversal = 94, Status = "Completed" };
        var quality = f.Stage with { Id = Guid.NewGuid(), StageKey = "quality", Status = "Completed" };
        stages.AddRange([readiness, quality, f.Stage]);
        f.PriorOutcomes.Add(new(readiness.Id, Guid.NewGuid(), WorkExecutionDispositions.Completed,
            "approved", "Exact candidate readiness receipt", JsonSerializer.SerializeToElement(new {}), [], []));
        f.PriorOutcomes.Add(new(quality.Id, Guid.NewGuid(), WorkExecutionDispositions.Completed,
            "passed", "Exact candidate independent QA receipt", JsonSerializer.SerializeToElement(new {}), [], []));
        f.Execution = f.Execution with { Candidate = f.Candidate, Stages = stages };
        string[] criteria = [f.Criterion, "Procedural source", "Final QA remains mandatory"];
        f.Scope = f.Scope with { Planning = f.Scope.Planning with { AcceptanceCriteria = criteria } };
        f.Plan = f.Plan with { Executions = [f.Execution], Scopes = [f.Scope] };
        var documentCopies = 0; var inspections = 0;
        using var client = new Client((call, messages) => {
            var text = messages[1].Text;
            Assert.True(text.Length < 100000);
            Assert.DoesNotContain("STALE_STATUS", text);
            documentCopies += text.Split("UNIQUE_DOCUMENT_BODY", StringSplitOptions.None).Length - 1;
            using var parsed = JsonDocument.Parse(text);
            var input = parsed.RootElement;
            if (input.TryGetProperty("parts", out var parts))
            {
                inspections++;
                return JsonSerializer.Serialize(new AggregateSourceReview.Report("Bounded source evidence",
                    inspections == 1 ? ["Retain the actual source defect"] : [],
                    parts.EnumerateArray().Select(p => p.GetProperty("key").GetString()!).ToArray()), Json);
            }
            var reference = input.GetProperty("reference");
            Assert.Equal(2, reference.GetProperty("priorOutcomes").GetArrayLength());
            Assert.Equal(JsonValueKind.Null, reference.GetProperty("source").ValueKind);
            Assert.False(reference.GetProperty("evidence").GetProperty("documents")[0].TryGetProperty("content", out _));
            Assert.Contains("independent QA receipt", text);
            if (input.TryGetProperty("assessments", out _))
                return "{\"summary\":\"Whole candidate assessed\",\"findings\":[]}";
            return Encode(new(f.Candidate.Digest, true, "Criteria assessed",
                input.GetProperty("acceptanceCriteria").EnumerateArray().Select(c => new WorkDeliveryCriterionResult(c.GetString()!, true, "Exact evidence")).ToArray(), []));
        });
        var result = await f.Run(client);
        Assert.True(inspections > 1); Assert.Equal(1, documentCopies);
        Assert.Equal("changes_requested", result.OutcomeCode);
        Assert.Contains("Retain the actual source defect", result.Output.Deserialize<WorkDeliveryReviewResult>(Json)!.Findings);
        Assert.Equal(inspections + 3, client.Calls);
    }
    private sealed class Fixture
    {
        internal readonly string Criterion = "Keep the exact criterion; downstream manager approval follows this review.";
        internal readonly Guid Reviewer = Guid.NewGuid();
        internal WorkDeliveryCandidate Candidate = new(new string('a', 64), 3, [], []);
        internal WorkStageExecutionResponse Stage;
        internal WorkDeliveryExecutionResponse Execution;
        internal WorkDeliveryScopeSnapshot Scope;
        internal WorkDeliveryPlanResponse Plan;
        internal int EvidenceReads;
        internal byte[]? Archive;
        internal IReadOnlyList<WorkDeliveryDocumentContent> Documents = [];
        internal List<WorkExecutionOutcomeV1> PriorOutcomes = [];
        private readonly AgentTestRuntime runtime;
        internal AgentTestRuntime Runtime => runtime;
        internal readonly Dictionary<string,AgentOperatingStateResponse> Ledger = new();
        internal bool AlterWrite;
        internal Fixture()
        {
            Stage = JsonSerializer.Deserialize<WorkStageExecutionResponse>("{}")! with { Id = Guid.NewGuid(), StageKey = "technical-review", Status = "Running", AgentInstallationId = Reviewer };
            Execution = JsonSerializer.Deserialize<WorkDeliveryExecutionResponse>("{}")! with { Id = Guid.NewGuid(), Scope = "Epic", ItemId = Guid.NewGuid(),
                Candidate = Candidate, CurrentStageKey = Stage.StageKey, Stages = [Stage] };
            Scope = new("Epic", Execution.ItemId, Guid.NewGuid(), 1, [], new(["Assess separate checklist without inventing extra criteria"], [Criterion]), []);
            Plan = JsonSerializer.Deserialize<WorkDeliveryPlanResponse>("{}")! with { Id = Guid.NewGuid(), Status = "Active", ScopeRevision = 3, Scopes = [Scope], Executions = [Execution] };
            runtime = new AgentTestRuntime()
                .RegisterCapability<ReadWorkDeliveryPlansRequest, IReadOnlyList<WorkDeliveryPlanResponse>>(WorkDeliveryCapabilities.Read, (_, _) => Task.FromResult<IReadOnlyList<WorkDeliveryPlanResponse>>([Plan]))
                .RegisterCapability<ReadWorkDeliveryEvidenceRequest, WorkDeliveryEvidenceResponse>(WorkDeliveryCapabilities.Evidence, (request, _) => {
                    EvidenceReads++; return Task.FromResult(new WorkDeliveryEvidenceResponse(Candidate, Documents) {
                        Archive = request.RepositoryId is not null ? Archive : null,
                        ArchiveSha256 = request.RepositoryId is not null && Archive is not null ? Convert.ToHexStringLower(SHA256.HashData(Archive)) : null,
                        CommitSha = request.RepositoryId is not null ? Candidate.Repositories.Single().CandidateCommitSha : null }); })
                .RegisterCapability<AgentOperatingStateReadRequest,AgentOperatingStateReadResponse>(PlatformCapabilities.AgentOperatingStateRead,
                    (r,_)=>Task.FromResult(new AgentOperatingStateReadResponse(Ledger.GetValueOrDefault(r.StateKey))))
                .RegisterCapability<AgentOperatingStateWriteRequest,AgentOperatingStateResponse>(PlatformCapabilities.AgentOperatingStateWrite,(r,_)=>{
                    if(Ledger.TryGetValue(r.StateKey,out var prior))return Task.FromResult(prior);
                    var payload=r.Payload;
                    if(AlterWrite) {
                        var checkpoint=payload.Deserialize<AggregateReviewChunks.Checkpoint>(Json)!;
                        payload=JsonSerializer.SerializeToElement(checkpoint with { Report=checkpoint.Report with { Summary="Different first writer judgment" } },Json);
                    }
                    var now=DateTimeOffset.UtcNow;
                    var saved=new AgentOperatingStateResponse(Guid.NewGuid(),r.StateKey,r.SchemaId,r.SchemaVersion,r.Status,r.SourceRevisions,
                        r.ConditionCodes,r.DecisionFingerprint,r.OpenCommitmentCorrelations,r.AttentionReviewId,payload,1,now,now);
                    Ledger.Add(r.StateKey,saved);return Task.FromResult(saved);
                });
        }
        internal WorkDeliveryReviewResult Report(bool approved) => new(Candidate.Digest, approved, "Independent substantive review",
            [new(Criterion, approved, "Actual candidate evidence")], approved ? [] : ["Keep this genuine unresolved defect"]);
        internal async Task<WorkExecutionOutcomeV1> Run(Client client)
        {
            var assignment = JsonSerializer.Deserialize<WorkExecutionAssignment>("{}")! with { Scope = "Epic", StageKey = Stage.StageKey,
                ExecutionId = Execution.Id, StageExecutionId = Stage.Id, AttemptId = Guid.NewGuid(), ItemId = Execution.ItemId,
                DeliveryPlanId = Plan.Id, ScopeRevision = 3, Candidate = Candidate, PriorOutcomes = PriorOutcomes };
            var result = await DeliveryScopeReview.ExecuteAsync(assignment, runtime.CreateContext(installationId: Reviewer.ToString()), client, default);
            return result.Value!.Value.Deserialize<WorkExecutionOutcomeV1>(Json)!;
        }
    }
    private sealed class Client(Func<int, IReadOnlyList<ChatMessage>, string> respond) : IChatClient
    {
        internal int Calls;
        public void Dispose() { }
        public object? GetService(Type type, object? key = null) => null;
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested(); Assert.NotNull(options!.ResponseFormat);
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, respond(++Calls, messages.ToArray()))));
        }
        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
