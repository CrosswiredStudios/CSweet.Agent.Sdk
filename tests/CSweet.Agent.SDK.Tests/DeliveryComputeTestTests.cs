using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CSweet.Agent.SDK.Compute;
using CSweet.WorkManagement.Contracts;

namespace CSweet.Agent.SDK.Tests;

public sealed class DeliveryComputeTestTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    internal static byte[] Archive(params string[] paths)
    {
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, true))
            foreach (var path in paths) { using var writer = new StreamWriter(zip.CreateEntry(path).Open()); writer.Write("test source"); }
        return buffer.ToArray();
    }
    private static ComputeDefaults Options(Guid project) => new("Ready", project, "linux-test", null)
    { Features = ["guest-execution","python","docker-apps-v1"], CachedImages = ["csweet/python:3.12","csweet/node:22"],
      LinkReachability = "local-machine", MaximumLifetimeSeconds = 3600 };

    [Theory]
    [InlineData("Dockerfile","docker")]
    [InlineData("index.html","static")]
    public void Selects_only_source_compatible_reported_compute_options(string path, string mode) =>
        Assert.Equal(mode, DeliveryComputeTest.SelectMode(Archive(path), Options(Guid.NewGuid())));
    [Theory]
    [InlineData("../index.html")]
    [InlineData("C:/index.html")]
    [InlineData("/index.html")]
    [InlineData(".git/config")]
    public void Rejects_unsafe_candidate_archives(string path) =>
        Assert.Throws<InvalidDataException>(() => DeliveryComputeTest.SelectMode(Archive(path), Options(Guid.NewGuid())));
    [Fact]
    public void Does_not_invent_docker_or_dependency_availability()
    {
        Assert.Throws<InvalidOperationException>(() => DeliveryComputeTest.SelectMode(Archive("Dockerfile"),
            Options(Guid.NewGuid()) with { Features = [] }));
        Assert.Throws<InvalidOperationException>(() => DeliveryComputeTest.SelectMode(Archive("index.html","package.json"),
            Options(Guid.NewGuid())));
    }

    [Theory]
    [InlineData("success","approved")]
    [InlineData("unknown","changes_requested")]
    [InlineData("wrong-generation","changes_requested")]
    [InlineData("denied","awaiting-compute")]
    [InlineData("pending-setup","awaiting-compute")]
    [InlineData("pending-command","awaiting-compute")]
    [InlineData("missing-marker","changes_requested")]
    [InlineData("expired-link","changes_requested")]
    public async Task Recovery_keeps_exact_requests_and_requires_real_command_and_link_receipts(string condition, string expected)
    {
        var project = Guid.NewGuid(); var employee = Guid.NewGuid(); var installation = Guid.NewGuid();
        var planId = Guid.NewGuid(); var executionId = Guid.NewGuid();
        var candidate = new WorkDeliveryCandidate(new string('a',64), 1,
            [new(Guid.NewGuid(),"release","main",new string('b',40),new string('c',40),new string('d',40))], []);
        var repository = candidate.Repositories.Single();
        var stage = JsonSerializer.Deserialize<WorkStageExecutionResponse>("{}")! with {
            Id = Guid.NewGuid(), StageKey = "build-readiness", AgentInstallationId = installation, OrganizationUserId = employee };
        var execution = JsonSerializer.Deserialize<WorkDeliveryExecutionResponse>("{}")! with {
            Id = executionId, CurrentStageKey = "build-readiness", Candidate = candidate, Stages = [stage] };
        var plan = JsonSerializer.Deserialize<WorkDeliveryPlanResponse>("{}")! with {
            Id = planId, WorkstreamId = project, Status = "Active", ScopeRevision = 1, Executions = [execution],
            Branches = [new(repository.RepositoryId,"Release",null,"release","main") { ReadinessMode = "compute-test" }],
            Scopes = [new("Release",null,Guid.NewGuid(),1,[],new(["Run source"],["HTTP works"]),[])] };
        var assignment = new WorkExecutionAssignment(executionId,stage.Id,Guid.NewGuid(),Guid.NewGuid(),project,Guid.NewGuid(),null,
            "Release",planId,1,null,null,1,"Release",null,"build-readiness",0,1,DateTimeOffset.UtcNow.AddHours(1),"Test",
            JsonSerializer.SerializeToElement(new {}),JsonSerializer.SerializeToElement(new {}),[],[],candidate)
            { OrganizationUserId = employee, AgentInstallationId = installation };
        var source = Archive("index.html"); var hash = Convert.ToHexStringLower(SHA256.HashData(source));
        AgentOperatingStateResponse? state = null;
        var environment = new ComputeEnvironment(Guid.NewGuid(),1,1,"running","ready","ephemeral",DateTimeOffset.UtcNow,DateTimeOffset.UtcNow.AddHours(1),null);
        var receipts = new Dictionary<Guid,ComputeOperation>();
        var requests = new Dictionary<string,ExecuteComputeCommandRequest>();
        var runtime = new AgentTestRuntime()
            .RegisterCapability<ReadWorkDeliveryPlansRequest,IReadOnlyList<WorkDeliveryPlanResponse>>(WorkDeliveryCapabilities.Read,(_,_) => Task.FromResult<IReadOnlyList<WorkDeliveryPlanResponse>>([plan]))
            .RegisterCapability<AgentOperatingStateReadRequest,AgentOperatingStateReadResponse>(PlatformCapabilities.AgentOperatingStateRead,
                (_,_) => Task.FromResult(new AgentOperatingStateReadResponse(state)))
            .RegisterCapability<AgentOperatingStateWriteRequest,AgentOperatingStateResponse>(PlatformCapabilities.AgentOperatingStateWrite,
                (r,_) => { Assert.Equal(state?.Revision,r.ExpectedRevision); var now=DateTimeOffset.UtcNow;
                    state=new(Guid.NewGuid(),r.StateKey,r.SchemaId,r.SchemaVersion,r.Status,r.SourceRevisions,r.ConditionCodes,r.DecisionFingerprint,
                        r.OpenCommitmentCorrelations,r.AttentionReviewId,r.Payload,(state?.Revision ?? 0)+1,now,now); return Task.FromResult(state); })
            .RegisterCapability<ProvisionComputeRequest,ComputeEnvironment>(ComputeCapabilities.Provision,(r,_) => {
                Assert.Equal(project,r.WorkstreamId); Assert.Null(r.Specification.Network); return Task.FromResult(environment); })
            .RegisterCapability<JsonElement,JsonElement>(ComputeCapabilities.Read,(r,token) => {
                object response = r.TryGetProperty("defaults",out _) ? Options(project) with { State=condition=="pending-setup" ? "Pending" : "Ready" } :
                    r.TryGetProperty("operationId",out var id) ? receipts[id.GetGuid()] : (object)environment;
                return Task.FromResult(JsonSerializer.SerializeToElement(response,Json)); })
            .RegisterCapability<ReadWorkDeliveryEvidenceRequest,WorkDeliveryEvidenceResponse>(WorkDeliveryCapabilities.Evidence,
                (_,_) => Task.FromResult(new WorkDeliveryEvidenceResponse(candidate,[],source,hash,repository.CandidateCommitSha)))
            .RegisterCapability<JsonElement,ComputeOperation>(ComputeCapabilities.Execute,(r,_) => {
                var command=r.GetProperty("workload").GetProperty("command").Deserialize<ComputeCommand>(Json)!;
                var request=new ExecuteComputeCommandRequest(r.GetProperty("environmentId").GetGuid(),
                    r.GetProperty("expectedGeneration").GetInt64(),r.GetProperty("idempotencyKey").GetString()!,command);
                if(requests.TryGetValue(request.IdempotencyKey,out var prior)) Assert.Equal(JsonSerializer.Serialize(prior,Json),JsonSerializer.Serialize(request,Json));
                requests[request.IdempotencyKey]=request;
                var validation=command.Arguments[1].Contains("docker run");
                var output=validation && condition!="missing-marker" ? DeliveryComputeTest.Marker(repository.CandidateCommitSha,hash) : "upload";
                var op=new ComputeOperation(Guid.NewGuid(),environment.Id,condition=="wrong-generation"?2:1,
                    condition=="pending-command" ? "Dispatching" : "Completed",null,
                    new(new(command.RequestId,condition=="unknown"?null:0,false,Encoding.UTF8.GetBytes(output),
                        ErrorCode:condition=="unknown"?"outcome-unknown":null)));
                receipts[op.Id]=op; return Task.FromResult(op); })
            .RegisterCapability<JsonElement,ComputeOperation>(ComputeCapabilities.PublishPort,(r,_) => {
                if(condition=="denied") throw new PlatformCapabilityException(ComputeCapabilities.PublishPort,PlatformCapabilityErrorCode.Denied,"grant missing");
                Assert.Equal(8080,r.GetProperty("workload").GetProperty("publishPort").GetInt32());
                var op=new ComputeOperation(Guid.NewGuid(),environment.Id,1,"Completed",null,
                    new(Url:"http://127.0.0.1:1234/",UrlExpiresAt:DateTimeOffset.UtcNow.AddHours(condition=="expired-link"?-1:1)));
                receipts[op.Id]=op;return Task.FromResult(op); });
        WorkExecutionOutcomeV1? outcome=null;
        // Fresh callback contexts model runtime loss; only the durable state and provider receipts survive.
        for(var pass=0;pass<5;pass++) {
            var result=await DeliveryBuildReadiness.ExecuteAsync(assignment,runtime.CreateContext(assignment.OrganizationId.ToString(),installation.ToString()),default);
            outcome=result.Value!.Value.Deserialize<WorkExecutionOutcomeV1>(Json)!;
            if(outcome.OutcomeCode!="awaiting-compute" || condition is "pending-setup" or "pending-command") break;
        }
        Assert.Equal(expected,outcome!.OutcomeCode);
        if(expected=="approved") {
            var proof=Assert.Single(outcome.Output.Deserialize<WorkDeliveryReviewResult>(Json)!.ComputeTests);
            Assert.Equal(repository.CandidateCommitSha,proof.CommitSha); Assert.Equal(hash,proof.ArchiveSha256);
            Assert.Equal(2,requests.Count);
        }
        if(condition=="pending-command") Assert.Single(requests);
    }
}
