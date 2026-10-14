using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CSweet.Agent.SDK.Compute;
using CSweet.WorkManagement.Contracts;

namespace CSweet.Agent.SDK;

/// <summary>Coordinates exact-source test compute through the broker, independent of certified packaging.</summary>
public static class DeliveryComputeTest
{
    public const string ExecutionCapability = "work.execution.compute-test.v1";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    internal sealed record State(Guid? EnvironmentId = null, string? ArchiveHash = null,
        int Offset = 0, int Step = 0, ExecuteComputeCommandRequest? Pending = null,
        string? PendingStage = null, Guid? OperationId = null, int NextOffset = 0,
        Guid? ValidationId = null, Guid? PublicationId = null, long Generation = 0,
        string? Mode = null);
    internal static async Task<AgentWorkResult> ExecuteAuthorizedAsync(WorkExecutionAssignment assignment,
        WorkDeliveryPlanResponse plan, AgentRuntimeContext context, CancellationToken ct)
    {
        var candidate = assignment.Candidate!;
        if (candidate.Repositories.Count != 1)
            throw new InvalidOperationException("Compute test delivery currently needs one repository; select a certified multi-repository build or split test deployments explicitly.");
        var repository = candidate.Repositories.Single();
        var key = $"delivery-compute:{assignment.ExecutionId:N}:{candidate.Digest}";
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
        try
        {
            var options = await context.Platform.Compute.GetProjectDefaultsAsync(plan.WorkstreamId, ct);
            if (options is not { State: "Ready", TemplateId: { } template })
                return Wait("Project compute preparation is pending: " + (options.ErrorCode ?? options.State));
            if (options.WorkstreamId != plan.WorkstreamId)
                throw new UnauthorizedAccessException("Compute defaults belong to another project.");
            if (!options.Features.Contains("guest-execution") || !options.Features.Contains("python") || !options.CachedImages.Contains("csweet/python:3.12"))
                return Wait("No compatible guest execution and source-transfer option is currently available.");
            if (state.EnvironmentId is null)
            {
                var desired = $"project-test:{plan.WorkstreamId:N}";
                var environment = await context.Platform.Compute.ProvisionAsync(new(plan.WorkstreamId, desired, desired,
                    new("linux", "x64", template, new(2, 2048, 20480), options.MaximumLifetimeSeconds)), ct);
                state = state with { EnvironmentId = environment.Id };
                await Save();
            }
            var current = await context.Platform.Compute.ReadAsync(state.EnvironmentId!.Value, ct);
            if (current.State != "ready" || current.LeaseExpiresAt <= DateTimeOffset.UtcNow)
                return Wait("Waiting for project test compute: " + (current.FailureCode ?? current.State));
            if (state.Generation != 0 && current.Generation != state.Generation)
                throw new InvalidOperationException("The test environment generation changed; inspect retained operations before rerunning.");
            if (state.Generation == 0) { state = state with { Generation = current.Generation }; await Save(); }
            if (state.Pending is { } pending)
            {
                // A lost submission response replays the exact persisted request/key. Known operations are read only.
                var operation = state.OperationId is { } operationId
                    ? await context.Platform.Compute.ReadOperationAsync(operationId, ct)
                    : await context.Platform.Compute.ExecuteAsync(pending, ct);
                if (state.OperationId is null) { state = state with { OperationId = operation.Id }; await Save(); }
                if (operation.EnvironmentId != current.Id || operation.Generation != state.Generation)
                    throw new InvalidOperationException("The compute operation does not match the assigned environment generation.");
                if (operation.Status is "Pending" or "Dispatching") return Wait("Waiting for " + state.PendingStage, operation.Id);
                if (operation.Status != "Completed" || operation.Result is not
                    { ErrorCode: null, Command: { ExitCode: 0, TimedOut: false, ErrorCode: null } result } ||
                    result.RequestId != pending.Command.RequestId)
                    throw new InvalidOperationException("Compute validation failed or its result is unknown: " +
                        (operation.Result?.Command?.StandardErrorText ?? operation.FailureCode ?? operation.Result?.ErrorCode));
                if (state.PendingStage == "validate" && !result.StandardOutputText.Contains(
                    Marker(repository.CandidateCommitSha, state.ArchiveHash!), StringComparison.Ordinal))
                    throw new InvalidOperationException("Compute validation omitted exact-source and HTTP health evidence.");
                state = state with { Offset = state.NextOffset, Step = state.Step + 1,
                    ValidationId = state.PendingStage == "validate" ? operation.Id : state.ValidationId,
                    Pending = null, PendingStage = null, OperationId = null };
                await Save();
            }
            if (state.ValidationId is null)
            {
                var evidence = await context.Platform.Work.ReadDeliveryEvidenceAsync(
                    new(plan.Id, assignment.ExecutionId, repository.RepositoryId), ct);
                if (evidence.Candidate.Digest != candidate.Digest || evidence.CommitSha != repository.CandidateCommitSha ||
                    evidence.Archive is not { Length: > 0 and <= 16777216 } archive ||
                    Convert.ToHexStringLower(SHA256.HashData(archive)) != evidence.ArchiveSha256)
                    throw new InvalidOperationException("The authorized source snapshot is not the exact assigned candidate.");
                if (state.ArchiveHash is not null && state.ArchiveHash != evidence.ArchiveSha256)
                    throw new InvalidOperationException("The immutable source archive changed.");
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
                else { script = ValidationScript(root, repository, state.ArchiveHash!, mode, plan.Id); step = "validate"; offset = state.Offset; }
                var requestId = new Guid(SHA256.HashData(Encoding.UTF8.GetBytes($"{key}:{state.Step}")).AsSpan(0,16));
                var request = new ExecuteComputeCommandRequest(current.Id, state.Generation, $"{key}:cmd:{state.Step}",
                    new(requestId, "/bin/sh", "/var/lib/csweet-compute/work", ["-c", script]));
                state = state with { Pending = request, PendingStage = step, NextOffset = offset };
                await Save();
                var submitted = await context.Platform.Compute.ExecuteAsync(request, ct);
                state = state with { OperationId = submitted.Id }; await Save();
                return Wait(step == "upload" ? "Transferring exact candidate source to team test compute." :
                    "Building and checking the candidate HTTP application.", submitted.Id);
            }
            var publication = state.PublicationId is { } publicationId
                ? await context.Platform.Compute.ReadOperationAsync(publicationId, ct)
                : await context.Platform.Compute.PublishPortAsync(new(current.Id, state.Generation, $"{key}:link", 8080), ct);
            if (state.PublicationId is null) { state = state with { PublicationId = publication.Id }; await Save(); }
            if (publication.EnvironmentId != current.Id || publication.Generation != state.Generation)
                throw new InvalidOperationException("The review link belongs to another environment generation.");
            if (publication.Status is "Pending" or "Dispatching") return Wait("Waiting for the provider-verified test link.", publication.Id);
            if (publication.Status != "Completed" || publication.Result is not
                { ErrorCode: null, Url: { } url, UrlExpiresAt: { } expiry } || expiry <= DateTimeOffset.UtcNow ||
                !Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "http" || uri.Host != "127.0.0.1")
                throw new InvalidOperationException("No current provider-verified local test URL was returned.");
            var proof = new WorkDeliveryComputeEvidence(repository.RepositoryId, repository.CandidateCommitSha,
                state.ArchiveHash!, current.Id, state.Generation, state.ValidationId.Value, publication.Id, url, expiry);
            var summary = $"Test application: {url} (available on the compute host; expires {expiry:O}). " +
                $"Exact source {repository.CandidateCommitSha}; {state.Mode} build and HTTP health passed. Independent gameplay/regression QA and technical acceptance still follow.";
            var scope = plan.Scopes.Single(x => x.Scope == assignment.Scope && x.ItemId == assignment.ItemId);
            return DeliveryScopeReview.Outcome(assignment, new(candidate.Digest, true, summary,
                scope.Planning.AcceptanceCriteria.Select(c => new WorkDeliveryCriterionResult(c, true,
                    "Compute build/HTTP readiness only: " + summary)).ToArray(), []) { ComputeTests = [proof] });
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
        { return Wait("Project test compute needs approved compute or local-link access. Open Compute and grant the test instance local link access; no public network access is requested."); }
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
            root.Any(c => c is not (>= 'a' and <= 'z' or >= '0' and <= '9' or '/' or '-')) || mode is not ("docker" or "static"))
            throw new ArgumentException("Use an owned guest review path, exact source hashes and a supported recipe.");
        var name = $"csweet-review-{repository.RepositoryId:N}";
        var serve = mode == "docker" ? $"""
docker build --network=none --pull=false -t {name}:test source
docker rm -f {name} >/dev/null 2>&1 || true
docker run -d --name {name} --network=bridge -p 127.0.0.1:8080:8080 --restart=no {name}:test
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
if [ -f source/package.json ]; then
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
}
