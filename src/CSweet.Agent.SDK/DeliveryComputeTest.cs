using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CSweet.Agent.SDK.Compute;
using CSweet.WorkManagement.Contracts;

namespace CSweet.Agent.SDK;

/// <summary>Coordinates exact-source test compute through the broker, independent of certified packaging.</summary>
public static partial class DeliveryComputeTest
{
    public const string ExecutionCapability = "work.execution.compute-test.v1";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    internal sealed record State(Guid? EnvironmentId = null, string? ArchiveHash = null,
        int Offset = 0, int Step = 0, ExecuteComputeCommandRequest? Pending = null,
        string? PendingStage = null, Guid? OperationId = null, int NextOffset = 0,
        Guid? ValidationId = null, Guid? PublicationId = null, long Generation = 0,
        string? Mode = null, long? PublicationExpectedGeneration = null,
        int ArchiveRestarts = 0, string? PreviousArchiveHash = null, Guid? RestoreId = null, bool? Networked = null,
        int EnvironmentAttempt = 0, string? RestoreJob = null, bool RestoreStarted = false);
    internal static async Task<AgentWorkResult> ExecuteAuthorizedAsync(WorkExecutionAssignment assignment,
        WorkDeliveryPlanResponse plan, AgentRuntimeContext context, CancellationToken ct)
    {
        var candidate = assignment.Candidate!;
        if (candidate.Repositories.Count != 1)
            throw new InvalidOperationException("Compute test delivery currently needs one repository; select a certified multi-repository build or split test deployments explicitly.");
        var repository = candidate.Repositories.Single();
        var stopped = plan.Executions.Single(e => e.Id == assignment.ExecutionId).Stages
            .LastOrDefault(s => s.LastOutcomeCode is "changes_requested" or "rejected");
        var epoch = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(candidate.Digest + ":" + (stopped?.Id.ToString("N") ?? "initial"))));
        var key = $"compute-node2:{assignment.ExecutionId:N}:{epoch}";
        var retained = await context.Platform.ReadOperatingStateAsync<State>(key, ct);
        var state = retained?.Payload ?? new();
        async Task Save()
        {
            retained = await context.Platform.WriteOperatingStateAsync(new WriteAgentOperatingStateRequest<State>(
                key, "work.delivery.compute-test.v1", 1, "Active", new Dictionary<string,string>(), [],
                "exact-candidate-test", [candidate.Digest], plan.WorkstreamId, state, retained?.Revision,
                $"{key}:{(retained?.Revision ?? 0) + 1}"), ct);
        }
        AgentWorkResult Wait(string reason, params Guid[] operations) => AgentWorkResult.Success(
            new WorkExecutionOutcomeV1(assignment.StageExecutionId, assignment.AttemptId,
                WorkExecutionDispositions.Completed, "awaiting-compute", reason,
                JsonSerializer.SerializeToElement(new WorkDeliveryComputePending(candidate.Digest, operations,
                    DateTimeOffset.UtcNow.AddMinutes(5)), Json), [], []));
        async Task<WorkDeliveryEvidenceResponse> Snapshot()
        {
            var evidence = await context.Platform.Work.ReadDeliveryEvidenceAsync(new(plan.Id, assignment.ExecutionId, repository.RepositoryId), ct);
            if (evidence.Candidate.Digest != candidate.Digest || evidence.CommitSha != repository.CandidateCommitSha ||
                evidence.Archive is not { Length: > 0 and <= 16777216 } archive ||
                Convert.ToHexStringLower(SHA256.HashData(archive)) != evidence.ArchiveSha256)
                throw new InvalidOperationException("The authorized source snapshot is not the exact assigned candidate.");
            return evidence;
        }
        try
        {
            var options = await context.Platform.Compute.GetProjectDefaultsAsync(plan.WorkstreamId, ct);
            if (options is not { State: "Ready", TemplateId: { } template })
                return Wait("Project compute preparation is pending: " + (options.ErrorCode ?? options.State));
            if (options.WorkstreamId != plan.WorkstreamId)
                throw new UnauthorizedAccessException("Compute defaults belong to another project.");
            if (!options.Features.Contains("guest-execution") || !options.Features.Contains("python") || !options.CachedImages.Contains("csweet/python:3.12"))
                return Wait("No compatible guest execution and source-transfer option is currently available.");
            if (state.Networked is null)
            {
                if (state.EnvironmentId is null)
                {
                    var source = await Snapshot(); var selected = SelectMode(source.Archive!, options);
                    state = state with { Networked = selected == "node", Mode = selected };
                }
                else state = state with { Networked = false }; // Retain already-submitted legacy request terms.
                await Save();
            }
            async Task Provision()
            {
                var desired = $"project-test{(state.Networked == true ? "-internet" : "")}:{plan.WorkstreamId:N}";
                if (state.EnvironmentAttempt > 0) desired += ":" + state.EnvironmentAttempt;
                var environment = await context.Platform.Compute.ProvisionAsync(new(plan.WorkstreamId, desired, desired,
                    new("linux", "x64", template, new(2, 2048, 20480), options.MaximumLifetimeSeconds,
                        Network: state.Networked == true ? new("outboundOnly", true) : null)), ct);
                state = state with { EnvironmentId = environment.Id };
                await Save();
            }
            if (state.EnvironmentId is null) await Provision();
            var current = await context.Platform.Compute.ReadAsync(state.EnvironmentId!.Value, ct);
            // Failed provisioning may trigger platform-owned teardown and advance the cursor.
            // Only confirmed destruction, before any guest command, permits a new stable attempt.
            while (current.State == "destroyed" && state.Step == 0 && state.Offset == 0 && state.Pending is null && state.EnvironmentAttempt < 3)
            {
                state = state with { EnvironmentId = null, Generation = 0, EnvironmentAttempt = state.EnvironmentAttempt + 1 };
                await Save(); await Provision();
                current = await context.Platform.Compute.ReadAsync(state.EnvironmentId!.Value, ct);
            }
            if (state.Step == 0 && state.Pending is null && (current.DesiredState == "destroyed" || current.State == "failed"))
                return Wait("Project compute provisioning needs provider recovery or confirmed cleanup before another bounded attempt: " + (current.FailureCode ?? current.State));
            if (current.LeaseExpiresAt <= DateTimeOffset.UtcNow)
                return Wait("Waiting for project test compute: " + (current.FailureCode ?? current.State));
            if (state.Generation == 0) { state = state with { Generation = current.Generation }; await Save(); }
            if (state.Pending is { } pending)
            {
                // A lost submission response replays the exact persisted request/key. Known operations are read only.
                var operation = state.OperationId is { } operationId
                    ? await context.Platform.Compute.ReadOperationAsync(operationId, ct)
                    : await context.Platform.Compute.ExecuteAsync(pending, ct);
                if (state.OperationId is null) { state = state with { OperationId = operation.Id }; await Save(); }
                // Submission advances the generation, including when its response was lost.
                current = await context.Platform.Compute.ReadAsync(current.Id, ct);
                if (operation.EnvironmentId != current.Id || operation.Generation != checked(pending.ExpectedGeneration + 1) ||
                    current.Generation != operation.Generation)
                    throw new InvalidOperationException("The compute operation does not match the assigned environment generation.");
                if (operation.Status is "Pending" or "Dispatching") return Wait("Waiting for " + state.PendingStage, operation.Id);
                if (state.PendingStage == "restore-start" && operation.Status == "Completed")
                {
                    // Launch acknowledgement is not installation success. An unknown reply
                    // leads to job observation, never another installation launch.
                    state = state with { Step=state.Step+1,Generation=operation.Generation,RestoreStarted=true,
                        Pending=null,PendingStage=null,OperationId=null };
                    await Save();
                }
                else if(state.PendingStage=="restore-observe"&&operation.Status=="Completed"&&
                    (operation.Result?.ErrorCode=="outcome-unknown"||operation.Result?.Command is {TimedOut:true} or {ErrorCode:"outcome-unknown"}))
                {
                    state=state with {Step=state.Step+1,Generation=operation.Generation,Pending=null,PendingStage=null,OperationId=null};
                    await Save();
                    return Wait("Dependency installation continues; its status check will resume. The installation is not restarted.");
                }
                else
                {
                if (operation.Status != "Completed" || operation.Result is not
                    { ErrorCode: null, Command: { ExitCode: 0, TimedOut: false, ErrorCode: null } result } ||
                    result.RequestId != pending.Command.RequestId)
                    throw new InvalidOperationException("Compute validation failed or its result is unknown: " +
                        (operation.Result?.Command?.StandardErrorText ?? operation.FailureCode ?? operation.Result?.ErrorCode));
                if (state.PendingStage == "validate" && !result.StandardOutputText.Contains(
                    Marker(repository.CandidateCommitSha, state.ArchiveHash!), StringComparison.Ordinal))
                    throw new InvalidOperationException("Compute validation omitted exact-source and HTTP health evidence.");
                var observingRestore=state.PendingStage=="restore-observe";
                var restoreRunning=observingRestore&&result.StandardOutputText.Contains(RestoreJobMarker(state.RestoreJob!,"running"),StringComparison.Ordinal);
                if(observingRestore&&!restoreRunning&&!result.StandardOutputText.Contains(RestoreJobMarker(state.RestoreJob!,"ok"),StringComparison.Ordinal))
                    throw new InvalidOperationException("Dependency installation did not return a completed job result.");
                state = state with { Offset = state.NextOffset, Step = state.Step + 1, Generation = operation.Generation,
                    RestoreId = state.PendingStage == "restore" || observingRestore&&!restoreRunning ? operation.Id : state.RestoreId,
                    ValidationId = state.PendingStage == "validate" ? operation.Id : state.ValidationId,
                    Pending = null, PendingStage = null, OperationId = null };
                await Save();
                if(restoreRunning)return AgentWorkResult.Success(new WorkExecutionOutcomeV1(assignment.StageExecutionId,assignment.AttemptId,
                    WorkExecutionDispositions.Completed,"awaiting-compute","Installing dependencies; the durable job continues without an overall download deadline.",
                    JsonSerializer.SerializeToElement(new WorkDeliveryComputePending(candidate.Digest,[],DateTimeOffset.UtcNow.AddSeconds(30)),Json),[],[]));
                }
            }
            if (state.PublicationExpectedGeneration is { } publicationExpected)
            {
                var publication = state.PublicationId is { } knownPublication
                    ? await context.Platform.Compute.ReadOperationAsync(knownPublication, ct)
                    : await context.Platform.Compute.PublishPortAsync(new(current.Id, publicationExpected, $"{key}:link", 8080), ct);
                if (state.PublicationId is null) { state = state with { PublicationId = publication.Id }; await Save(); }
                current = await context.Platform.Compute.ReadAsync(current.Id, ct);
                if (publication.EnvironmentId != current.Id || publication.Generation != checked(publicationExpected + 1) ||
                    current.Generation != publication.Generation)
                    throw new InvalidOperationException("The publication receipt does not match the retained request and current environment.");
                if (publication.Status is "Pending" or "Dispatching") return Wait("Waiting for the local test link.", publication.Id);
                if (publication.Status != "Completed" || publication.Result is not
                    { ErrorCode: null, Url: { } url, UrlExpiresAt: { } expiry } || expiry <= DateTimeOffset.UtcNow ||
                    !Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "http" || uri.Host != "127.0.0.1" ||
                    current.State != "ready" || current.LeaseExpiresAt <= DateTimeOffset.UtcNow || state.ValidationId is null)
                    throw new InvalidOperationException("No current provider-verified local test URL was returned.");
                var proof = new WorkDeliveryComputeEvidence(repository.RepositoryId, repository.CandidateCommitSha,
                    state.ArchiveHash!, current.Id, publication.Generation, state.ValidationId.Value, publication.Id, url, expiry);
                var summary = $"Test application: {url} (available on the compute host; expires {expiry:O}). " +
                    $"Exact source {repository.CandidateCommitSha}; {state.Mode} build and HTTP health passed. Independent gameplay/regression QA and technical acceptance still follow.";
                var scope = plan.Scopes.Single(x => x.Scope == assignment.Scope && x.ItemId == assignment.ItemId);
                return DeliveryScopeReview.Outcome(assignment, new(candidate.Digest, true, summary,
                    scope.Planning.AcceptanceCriteria.Select(c => new WorkDeliveryCriterionResult(c, true,
                        "Compute build/HTTP readiness only: " + summary)).ToArray(), []) { ComputeTests = [proof] });
            }
            if (current.Generation != state.Generation)
                throw new InvalidOperationException("The test environment generation changed without a matching retained operation; inspect current operations before rerunning.");
            if (current.State != "ready") return Wait("Waiting for project test compute: " + (current.FailureCode ?? current.State));
            if (state.ValidationId is null)
            {
                var evidence = await Snapshot(); var archive = evidence.Archive!;
                if (state.ArchiveHash is not null && state.ArchiveHash != evidence.ArchiveSha256)
                {
                    // Old source serializers stamped download time into ZIP headers.
                    // Only a completed upload phase may restart, once, before any
                    // validation/publication command. Keep Step so request keys and
                    // old receipts are never overwritten or replayed with new bytes.
                    if (state.ArchiveRestarts != 0 || state.Pending is not null || state.ValidationId is not null ||
                        state.PublicationId is not null || state.PublicationExpectedGeneration is not null)
                        throw new InvalidOperationException("The immutable source archive changed repeatedly or after validation; inspect retained operations.");
                    state = state with { Offset = 0, PreviousArchiveHash = state.ArchiveHash,
                        ArchiveHash = evidence.ArchiveSha256, ArchiveRestarts = 1 };
                    await Save();
                }
                var mode = SelectMode(archive, options);
                if (state.Mode is not null && state.Mode != mode) throw new InvalidOperationException("The selected compute recipe changed.");
                state = state with { ArchiveHash = evidence.ArchiveSha256, Mode = mode };
                var root = $"/var/lib/csweet-compute/work/review-{repository.RepositoryId:N}-{candidate.Digest[..16]}";
                string script; string step; int offset;
                if (state.Offset < archive.Length)
                {
                    var count = Math.Min(12000, archive.Length - state.Offset);
                    script = $"set -eu\nmkdir -p {root}\nprintf '%s' '{Convert.ToBase64String(archive, state.Offset, count)}' | base64 -d | dd of={root}/source.zip bs=1 seek={state.Offset} conv=notrunc status=none\n";
                    step = "upload"; offset = state.Offset + count;
                }
                else if (mode == "node" && state.RestoreId is null)
                {
                    var lockfile=DeliveryNodeDependencies.LockfileHash(archive);
                    if(state.Networked==true) {
                        state=state with {RestoreJob=state.RestoreJob??NetworkRestoreJobKey(key)};await Save();
                        script=state.RestoreStarted?ObserveNetworkRestoreScript(root,state.ArchiveHash!,lockfile,state.RestoreJob):
                            StartNetworkRestoreScript(root,state.ArchiveHash!,lockfile,state.RestoreJob);
                        step=state.RestoreStarted?"restore-observe":"restore-start";
                    } else {script=RestoreScript(root,state.ArchiveHash!,lockfile);step="restore";}
                    offset=state.Offset;
                }
                else { script = ValidationScript(root, repository, state.ArchiveHash!, mode, plan.Id); step = "validate"; offset = state.Offset; }
                var requestId = new Guid(SHA256.HashData(Encoding.UTF8.GetBytes($"{key}:{state.Step}")).AsSpan(0,16));
                var request = new ExecuteComputeCommandRequest(current.Id, state.Generation, $"{key}:cmd:{state.Step}",
                    new(requestId, "/bin/sh", "/var/lib/csweet-compute/work", ["-c", script]),
                    step == "restore" && state.Networked != true ? DeliveryNodeDependencies.Read(archive) : null);
                state = state with { Pending = request, PendingStage = step, NextOffset = offset };
                await Save();
                var submitted = await context.Platform.Compute.ExecuteAsync(request, ct);
                if (submitted.EnvironmentId != current.Id || submitted.Generation != checked(request.ExpectedGeneration + 1))
                    throw new InvalidOperationException("The submitted compute receipt does not match the retained request.");
                state = state with { OperationId = submitted.Id }; await Save();
                return Wait(step == "upload" ? "Transferring exact candidate source to team test compute." :
                    "Building and checking the candidate HTTP application.", submitted.Id);
            }
            // Save exact publication terms before the external submission.
            state = state with { PublicationExpectedGeneration = state.Generation }; await Save();
            var linkOperation = await context.Platform.Compute.PublishPortAsync(new(current.Id, state.Generation, $"{key}:link", 8080), ct);
            if (linkOperation.EnvironmentId != current.Id || linkOperation.Generation != checked(state.Generation + 1))
                throw new InvalidOperationException("The submitted publication receipt does not match the retained request.");
            state = state with { PublicationId = linkOperation.Id }; await Save();
            return Wait("Waiting for the local test link.", linkOperation.Id);
        }
        catch (Exception error) when (error is InvalidOperationException or InvalidDataException)
        {
            var scope = plan.Scopes.Single(x => x.Scope == assignment.Scope && x.ItemId == assignment.ItemId);
            return DeliveryScopeReview.Outcome(assignment, new(candidate.Digest, false, error.Message,
                scope.Planning.AcceptanceCriteria.Select(c => new WorkDeliveryCriterionResult(c, false,
                    "Test readiness needs remediation: " + error.Message)).ToArray(), [error.Message]));
        }
        catch (PlatformCapabilityException error) when (error.Code == PlatformCapabilityErrorCode.Denied ||
            error.FailureCode == "compute_authority_denied")
        { return Wait(state.Networked == true
            ? "Project development compute needs current compute and network.outbound.v1 grants for an outbound-network instance. No public endpoint is requested."
            : state.PendingStage == "restore"
            ? "Dependency restore needs current compute.execute.v1 and network.outbound.v1 grants scoped to this project/environment and registry.npmjs.org. Source and lockfile remain pinned; no public endpoint is requested."
            : "Project test compute needs approved compute or local-link access. Open Compute and grant the test instance local link access; no public network access is requested."); }
    }

    public static string SelectMode(byte[] archive, ComputeDefaults options)
    {
        using var zip = new ZipArchive(new MemoryStream(archive), ZipArchiveMode.Read);
        if (zip.Entries.Count > 4096 || zip.Entries.Sum(x => x.Length) > 16777216)
            throw new InvalidDataException("The test source exceeds compute transfer limits.");
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in zip.Entries)
            if (!names.Add(entry.FullName) || entry.FullName.StartsWith('/') || entry.FullName.Contains('\\') || entry.FullName.Contains(':') ||
                entry.FullName.Split('/').Any(x => x is "." or ".." or ".git") ||
                ((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000)
                throw new InvalidDataException("The test source contains unsafe paths or links.");
        if (zip.GetEntry("package.json") is { } package && zip.GetEntry("package-lock.json") is not null &&
            options.Features.Contains("docker-apps-v1") && options.CachedImages.Contains("csweet/node:22"))
        {
            using var stream = package.Open(); using var json = JsonDocument.Parse(stream);
            if (json.RootElement.TryGetProperty("scripts", out var scripts) && scripts.TryGetProperty("build", out var build) &&
                build.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(build.GetString())) return "node";
        }
        if (zip.GetEntry("Dockerfile") is not null && options.Features.Contains("docker-apps-v1") && options.CachedImages.Contains("csweet/node:22"))
            return "docker";
        if (zip.GetEntry("index.html") is not null && zip.GetEntry("package.json") is null)
            return "static";
        throw new InvalidOperationException("The candidate needs a root Dockerfile compatible with available cached images, or a dependency-free static index.html. Ask the Developer to supply a runnable test package.");
    }
    public static string Marker(string commit, string archiveHash) => $"csweet-candidate:{commit}:{archiveHash}:http-ok";
    public static string ValidationScript(string root, WorkDeliveryRepositoryCandidate repository,
        string archiveHash, string mode, Guid planId)
    {
        if (archiveHash.Length != 64 || archiveHash.Any(c => !char.IsAsciiHexDigit(c) || char.IsUpper(c)) ||
            repository.CandidateCommitSha.Length != 40 || repository.CandidateCommitSha.Any(c => !char.IsAsciiHexDigit(c) || char.IsUpper(c)) ||
            !root.StartsWith("/var/lib/csweet-compute/work/review-", StringComparison.Ordinal) ||
            root.Any(c => c is not (>= 'a' and <= 'z' or >= '0' and <= '9' or '/' or '-')) || mode is not ("docker" or "static" or "node"))
            throw new ArgumentException("Use an owned guest review path, exact source hashes and a supported recipe.");
        var name = $"csweet-review-{repository.RepositoryId:N}";
        var serve = mode == "docker" ? $"""
docker build --network=none --pull=false -t {name}:test source
docker rm -f {name} >/dev/null 2>&1 || true
docker run -d --name {name} --network=bridge -p 127.0.0.1:8080:8080 --restart=no {name}:test
""" : mode == "node" ? $"""
if ! docker run --rm --network=none -v "$PWD/source:/app" -w /app csweet/node:22 sh -c 'npm test && npm run build' > validation.log 2>&1; then
 tail -c 6000 validation.log >&2
 exit 1
fi
test -f source/dist/index.html
docker rm -f {name} >/dev/null 2>&1 || true
docker run -d --name {name} --network=bridge -p 127.0.0.1:8080:8080 --restart=no -v "$PWD/source/dist:/app:ro" -w /app csweet/python:3.12 python -m http.server 8080 --bind 0.0.0.0
""" : $"""
docker rm -f {name} >/dev/null 2>&1 || true
docker run -d --name {name} --network=bridge -p 127.0.0.1:8080:8080 --restart=no -v "$PWD/source:/app:ro" -w /app csweet/python:3.12 python -m http.server 8080 --bind 0.0.0.0
""";
        return ($"""
set -eu
cd {root}
printf '%s  source.zip\n' '{archiveHash}' | sha256sum -c -
/usr/bin/python3 - <<'PY'
import zipfile, pathlib, stat
root=pathlib.Path('source')
with zipfile.ZipFile('source.zip') as z:
 if len(z.infolist())>4096 or sum(x.file_size for x in z.infolist())>16777216: raise ValueError('source limit')
 for e in z.infolist():
  p=pathlib.PurePosixPath(e.filename)
  if p.is_absolute() or any(x in ('.','..','.git') for x in e.filename.split('/')) or '\\' in e.filename or ':' in e.filename or stat.S_ISLNK(e.external_attr>>16): raise ValueError('unsafe source')
 z.extractall(root)
PY
if [ '{mode}' != node ] && [ -f source/package.json ]; then
 docker run --rm --network=none -v "$PWD/source:/app" -w /app csweet/node:22 npm --offline test
fi
{serve}
/usr/bin/python3 - <<'PY'
import time,urllib.request
for attempt in range(30):
 try:
  with urllib.request.urlopen('http://127.0.0.1:8080/',timeout=1) as r:
   assert r.status==200 and r.read(4096)
  print('{Marker(repository.CandidateCommitSha, archiveHash)}')
  break
 except Exception:
  if attempt==29: raise
  time.sleep(.25)
PY
""").Replace("\r\n", "\n");
    }

    public static string RestoreScript(string root, string archiveHash, string lockfileHash) => RestoreScript(root, archiveHash, lockfileHash, false);

    public static string RestoreScript(string root, string archiveHash, string lockfileHash, bool networked)
    {
        if (!root.StartsWith("/var/lib/csweet-compute/work/review-", StringComparison.Ordinal) ||
            root.Any(c => c is not (>= 'a' and <= 'z' or >= '0' and <= '9' or '/' or '-')) ||
            new[] { archiveHash, lockfileHash }.Any(hash => hash.Length != 64 || hash.Any(c => !"0123456789abcdef".Contains(c))))
            throw new ArgumentException("Use exact source and lockfile hashes in an owned guest workspace.");
        var install = networked
            ? "docker run --rm --network=bridge -v \"$PWD/source:/app\" -w /app csweet/node:22 npm ci --no-audit --no-fund"
            : $"docker run --rm --network=none -v \"$PWD/source:/app\" -v /var/lib/csweet-compute/work/npm-{lockfileHash}/cache:/npm-cache -w /app csweet/node:22 npm ci --offline --cache /npm-cache --no-audit --no-fund";
        return ($"""
set -eu
cd {root}
printf '%s  source.zip\n' '{archiveHash}' | sha256sum -c -
/usr/bin/python3 - <<'PY'
import zipfile,pathlib,stat
with zipfile.ZipFile('source.zip') as z:
 if len(z.infolist())>4096 or sum(e.file_size for e in z.infolist())>16777216: raise ValueError('source limit')
 for e in z.infolist():
  p=pathlib.PurePosixPath(e.filename)
  if p.is_absolute() or any(x in ('.','..','.git') for x in e.filename.split('/')) or '\\' in e.filename or ':' in e.filename or stat.S_ISLNK(e.external_attr>>16): raise ValueError('unsafe source')
 z.extractall('source')
PY
printf '%s  source/package-lock.json\n' '{lockfileHash}' | sha256sum -c -
{install}
""").Replace("\r\n", "\n");
    }
}
