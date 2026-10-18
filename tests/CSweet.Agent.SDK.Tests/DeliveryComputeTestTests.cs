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
    [InlineData("lost-command-response","approved")]
    [InlineData("lost-publication-response","approved")]
    [InlineData("intervening-generation","changes_requested")]
    [InlineData("pending-publication","awaiting-compute")]
    [InlineData("archive-changes-once","approved")]
    [InlineData("archive-keeps-changing","changes_requested")]
    [InlineData("node-success","approved")]
    [InlineData("node-denied","awaiting-compute")]
    [InlineData("node-restore-failed","changes_requested")]
    [InlineData("node-unknown","changes_requested")]
    [InlineData("lost-restore","approved")]
    [InlineData("node-network-success","approved")]
    [InlineData("node-slow-job","approved")]
    [InlineData("node-start-unknown-finished","approved")]
    [InlineData("node-observe-unknown","approved")]
    [InlineData("node-network-denied","awaiting-compute")]
    [InlineData("node-retired-environment","approved")]
    [InlineData("node-cleanup-in-progress","awaiting-compute")]
    [InlineData("node-repeated-retired","awaiting-compute")]
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
        var node = condition.StartsWith("node-", StringComparison.Ordinal) || condition == "lost-restore";
        var networked = node;
        var source = node ? NodeArchive() : Archive("index.html"); var hash = Convert.ToHexStringLower(SHA256.HashData(source));
        AgentOperatingStateResponse? state = null;
        var environment = new ComputeEnvironment(Guid.NewGuid(),1,1,"running","ready","ephemeral",DateTimeOffset.UtcNow,DateTimeOffset.UtcNow.AddHours(1),null);
        var receipts = new Dictionary<Guid,ComputeOperation>();
        var requests = new Dictionary<string,ExecuteComputeCommandRequest>();
        var accepted = new Dictionary<string,ComputeOperation>();
        var publicationRequests = new Dictionary<string,long>();
        var lost = false;
        var deniedRestore = false;
        var archiveMutations = 0;
        var provisions = 0;
        var runtime = new AgentTestRuntime()
            .RegisterCapability<ReadWorkDeliveryPlansRequest,IReadOnlyList<WorkDeliveryPlanResponse>>(WorkDeliveryCapabilities.Read,(_,_) => Task.FromResult<IReadOnlyList<WorkDeliveryPlanResponse>>([plan]))
            .RegisterCapability<AgentOperatingStateReadRequest,AgentOperatingStateReadResponse>(PlatformCapabilities.AgentOperatingStateRead,
                (_,_) => Task.FromResult(new AgentOperatingStateReadResponse(state)))
            .RegisterCapability<AgentOperatingStateWriteRequest,AgentOperatingStateResponse>(PlatformCapabilities.AgentOperatingStateWrite,
                (r,_) => { Assert.Equal(state?.Revision,r.ExpectedRevision); var now=DateTimeOffset.UtcNow;
                    state=new(Guid.NewGuid(),r.StateKey,r.SchemaId,r.SchemaVersion,r.Status,r.SourceRevisions,r.ConditionCodes,r.DecisionFingerprint,
                        r.OpenCommitmentCorrelations,r.AttentionReviewId,r.Payload,(state?.Revision ?? 0)+1,now,now); return Task.FromResult(state); })
            .RegisterCapability<ProvisionComputeRequest,ComputeEnvironment>(ComputeCapabilities.Provision,(r,_) => {
                provisions++;
                Assert.Equal(project,r.WorkstreamId);
                if (networked) {
                    Assert.Equal("outboundOnly", r.Specification.Network!.Mode); Assert.True(r.Specification.Network.AllowOutbound);
                    Assert.False(r.Specification.Network.PublicEndpoint); Assert.Empty(r.Specification.Network.PublishedPorts!);
                    Assert.Contains("project-test-internet:", r.DesiredEnvironmentKey);
                    if (condition is "node-network-denied" or "node-denied") { deniedRestore = true; throw new PlatformCapabilityException(ComputeCapabilities.Provision, PlatformCapabilityErrorCode.Denied, "outbound grant missing"); }
                } else Assert.Null(r.Specification.Network);
                if(condition=="node-retired-environment") {
                    if(provisions==1)environment=environment with {State="destroyed",DesiredState="destroyed",Generation=2};
                    else {Assert.EndsWith(":1",r.DesiredEnvironmentKey);environment=environment with {Id=Guid.NewGuid(),State="ready",DesiredState="running",Generation=1};}
                }
                if(condition=="node-cleanup-in-progress")environment=environment with {State="failed",DesiredState="destroyed",Generation=2};
                if(condition=="node-repeated-retired")environment=environment with {State="destroyed",DesiredState="destroyed",Generation=2};
                return Task.FromResult(environment); })
            .RegisterCapability<JsonElement,JsonElement>(ComputeCapabilities.Read,(r,token) => {
                object response = r.TryGetProperty("defaults",out _) ? Options(project) with {
                    State=condition=="pending-setup" ? "Pending" : "Ready",
                    Features = networked ? Options(project).Features.Append("outbound-network-v1").ToArray() : Options(project).Features } :
                    r.TryGetProperty("operationId",out var id) ? receipts[id.GetGuid()] : (object)environment;
                return Task.FromResult(JsonSerializer.SerializeToElement(response,Json)); })
            .RegisterCapability<ReadWorkDeliveryEvidenceRequest,WorkDeliveryEvidenceResponse>(WorkDeliveryCapabilities.Evidence,
                (_,_) => Task.FromResult(new WorkDeliveryEvidenceResponse(candidate,[],source,hash,repository.CandidateCommitSha)))
            .RegisterCapability<JsonElement,ComputeOperation>(ComputeCapabilities.Execute,(r,_) => {
                var command=r.GetProperty("workload").GetProperty("command").Deserialize<ComputeCommand>(Json)!;
                var dependencies = r.GetProperty("workload").TryGetProperty("nodeDependencies", out var dependencyJson)
                    ? dependencyJson.Deserialize<ComputeNodeDependencies>(Json) : null;
                if (dependencies is not null && condition == "node-denied") {
                    deniedRestore = true;
                    throw new PlatformCapabilityException(ComputeCapabilities.Execute, PlatformCapabilityErrorCode.Denied, "outbound grant missing");
                }
                var request=new ExecuteComputeCommandRequest(r.GetProperty("environmentId").GetGuid(),
                    r.GetProperty("expectedGeneration").GetInt64(),r.GetProperty("idempotencyKey").GetString()!,command,dependencies);
                if(requests.TryGetValue(request.IdempotencyKey,out var prior)) {
                    Assert.Equal(JsonSerializer.Serialize(prior,Json),JsonSerializer.Serialize(request,Json));
                    return Task.FromResult(accepted[request.IdempotencyKey]);
                }
                Assert.Equal(environment.Generation,request.ExpectedGeneration);
                requests[request.IdempotencyKey]=request;
                var validation=command.Arguments[1].Contains("csweet-candidate:");
                var restoring=command.Arguments[1].Contains("npm ci", StringComparison.Ordinal);
                var observing=command.Arguments[1].Contains("--property=ActiveState",StringComparison.Ordinal);
                var output=validation && condition!="missing-marker" ? DeliveryComputeTest.Marker(repository.CandidateCommitSha,hash) : "upload";
                var failedObservation=observing&&condition is "node-restore-failed" or "node-unknown";
                var unknownObservation=observing&&condition=="node-observe-unknown"&&requests.Values.Count(r=>r.Command.Arguments[1].Contains("--property=ActiveState"))==1;
                if(observing) {
                    var job=state!.Payload.Deserialize<DeliveryComputeTest.State>(Json)!.RestoreJob!;
                    var running=condition=="node-slow-job"&&requests.Values.Count(r=>r.Command.Arguments[1].Contains("--property=ActiveState"))<3;
                    output=DeliveryComputeTest.RestoreJobMarker(job,running?"running":"ok");
                }
                var generation=environment.Generation+1;
                var op=new ComputeOperation(Guid.NewGuid(),environment.Id,condition=="wrong-generation"?generation+1:generation,
                    condition=="pending-command" ? "Dispatching" : "Completed",null,
                    new(new(command.RequestId,unknownObservation||condition=="unknown" || condition is ("node-unknown" or "node-start-unknown-finished") && restoring ?null:
                        condition=="node-restore-failed" && restoring || failedObservation ? 1 : 0,false,Encoding.UTF8.GetBytes(output),
                        ErrorCode:unknownObservation||condition=="unknown" || condition is ("node-unknown" or "node-start-unknown-finished") && restoring ?"outcome-unknown":null)));
                environment=environment with {Generation=condition=="intervening-generation"?generation+1:generation,
                    State=condition=="pending-command"?"busy":"ready"};
                receipts[op.Id]=op;accepted[request.IdempotencyKey]=op;
                if(!validation && (condition=="archive-keeps-changing" || condition=="archive-changes-once"&&archiveMutations==0)) {
                    source=WithTimestamp(source,1980+archiveMutations++);hash=Convert.ToHexStringLower(SHA256.HashData(source));
                }
                if(condition=="lost-command-response"&&!lost){lost=true;throw new OperationCanceledException("Submission succeeded; response lost");}
                if(condition=="lost-restore" && restoring && !lost){lost=true;throw new OperationCanceledException("Restore accepted; response lost");}
                return Task.FromResult(op); })
            .RegisterCapability<JsonElement,ComputeOperation>(ComputeCapabilities.PublishPort,(r,_) => {
                if(condition=="denied") throw new PlatformCapabilityException(ComputeCapabilities.PublishPort,PlatformCapabilityErrorCode.Denied,"grant missing");
                Assert.Equal(8080,r.GetProperty("workload").GetProperty("publishPort").GetInt32());
                var requestKey=r.GetProperty("idempotencyKey").GetString()!;
                var expectedGeneration=r.GetProperty("expectedGeneration").GetInt64();
                if(publicationRequests.TryGetValue(requestKey,out var priorGeneration)) {
                    Assert.Equal(priorGeneration,expectedGeneration);return Task.FromResult(accepted[requestKey]);
                }
                Assert.Equal(environment.Generation,expectedGeneration);publicationRequests[requestKey]=expectedGeneration;
                environment=environment with {Generation=environment.Generation+1,State=condition=="pending-publication"?"busy":"ready"};
                var op=new ComputeOperation(Guid.NewGuid(),environment.Id,environment.Generation,condition=="pending-publication"?"Dispatching":"Completed",null,
                    new(Url:"http://127.0.0.1:1234/",UrlExpiresAt:DateTimeOffset.UtcNow.AddHours(condition=="expired-link"?-1:1)));
                receipts[op.Id]=op;accepted[requestKey]=op;
                if(condition=="lost-publication-response"&&!lost){lost=true;throw new OperationCanceledException("Publication succeeded; response lost");}
                return Task.FromResult(op); });
        WorkExecutionOutcomeV1? outcome=null;
        // Fresh callback contexts model runtime loss; only the durable state and provider receipts survive.
        for(var pass=0;pass<24;pass++) {
            AgentWorkResult result;
            try {result=await DeliveryBuildReadiness.ExecuteAsync(assignment,runtime.CreateContext(assignment.OrganizationId.ToString(),installation.ToString()),default);}
            catch(OperationCanceledException) when(condition.StartsWith("lost-",StringComparison.Ordinal)){continue;}
            outcome=result.Value!.Value.Deserialize<WorkExecutionOutcomeV1>(Json)!;
            if(outcome.OutcomeCode!="awaiting-compute" || deniedRestore || condition is "pending-setup" or "pending-command" or "node-network-denied" or "node-cleanup-in-progress" or "node-repeated-retired" ||
                condition=="pending-publication"&&publicationRequests.Count>0) break;
        }
        Assert.Equal(expected,outcome!.OutcomeCode);
        if(condition=="node-retired-environment")Assert.Equal(2,provisions);
        if(condition is "node-cleanup-in-progress" or "node-repeated-retired") {Assert.Empty(requests);Assert.Equal(condition=="node-repeated-retired"?4:1,provisions);}
        if(expected=="approved") {
            var proof=Assert.Single(outcome.Output.Deserialize<WorkDeliveryReviewResult>(Json)!.ComputeTests);
            Assert.Equal(repository.CandidateCommitSha,proof.CommitSha); Assert.Equal(hash,proof.ArchiveSha256);
            var restarted=condition=="archive-changes-once";
            var extra=condition=="node-slow-job"?2:condition=="node-observe-unknown"?1:0;
            Assert.Equal(node?4+extra:restarted?3:2,requests.Count);
            Assert.Equal(node?6+extra:restarted?5:4,proof.Generation);Assert.Equal(proof.Generation-1,receipts[proof.ValidationOperationId].Generation);
            Assert.Equal(proof.Generation,receipts[proof.PublicationOperationId].Generation);Assert.Single(publicationRequests);
            if(restarted) {var saved=state!.Payload.Deserialize<DeliveryComputeTest.State>(Json)!;
                Assert.Equal(1,saved.ArchiveRestarts);Assert.NotNull(saved.PreviousArchiveHash);Assert.NotEqual(saved.PreviousArchiveHash,saved.ArchiveHash);}
        }
        if(condition=="pending-command") Assert.Single(requests);
        if(condition=="archive-keeps-changing") {Assert.Equal(2,requests.Count);Assert.Empty(publicationRequests);}
        if(node && !networked && !deniedRestore) Assert.Single(requests.Values, r => r.NodeDependencies is not null);
        if(networked) {
            Assert.All(requests.Values, r => Assert.Null(r.NodeDependencies));
            if (expected == "approved") {
                var restore = Assert.Single(requests.Values, r => r.Command.Arguments[1].Contains("npm ci"));
                Assert.Contains("--network=bridge", restore.Command.Arguments[1]);
                Assert.DoesNotContain("--offline", restore.Command.Arguments[1]);
                Assert.DoesNotContain("/npm-cache", restore.Command.Arguments[1]);
                Assert.Contains("RuntimeMaxSec=infinity",restore.Command.Arguments[1]);
                Assert.Single(requests.Values,r=>r.Command.Arguments[1].Contains("systemd-run --no-block --unit=csweet-npm-"));
            } else if (deniedRestore) Assert.Empty(requests);
        }
        if(deniedRestore) Assert.Contains("network.outbound.v1", outcome!.Summary);
        if(condition is "node-restore-failed" or "node-unknown") Assert.Empty(publicationRequests);
    }

    internal static byte[] NodeArchive(string? lockfile = null)
    {
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, true)) {
            using(var writer=new StreamWriter(zip.CreateEntry("package.json").Open())) writer.Write("""{"scripts":{"test":"node test.mjs","build":"node build.mjs"}}""");
            using(var writer=new StreamWriter(zip.CreateEntry("package-lock.json").Open())) writer.Write(lockfile ?? """{"lockfileVersion":3,"packages":{"":{}}}""");
        }
        return buffer.ToArray();
    }

    private static byte[] WithTimestamp(byte[] source,int year)
    {
        using var buffer=new MemoryStream();using var original=new ZipArchive(new MemoryStream(source),ZipArchiveMode.Read);
        using(var zip=new ZipArchive(buffer,ZipArchiveMode.Create,true))
            foreach(var entry in original.Entries) {var changed=zip.CreateEntry(entry.FullName);changed.LastWriteTime=new DateTimeOffset(year,1,1,0,0,0,TimeSpan.Zero);
                using var input=entry.Open();using var output=changed.Open();input.CopyTo(output);}
        return buffer.ToArray();
    }
}
