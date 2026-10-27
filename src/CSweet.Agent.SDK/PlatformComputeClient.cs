using CSweet.Agent.SDK.Compute;

namespace CSweet.Agent.SDK;

/// <summary>Typed compute requests through the existing grant-governed platform broker.
/// Methods submit once; callers use stable keys, current generations, and durable change events.</summary>
public sealed class PlatformComputeClient
{
    private readonly PlatformCapabilityClient _platform;
    internal PlatformComputeClient(PlatformCapabilityClient platform) => _platform = platform;

    public Task<ComputeEnvironment> ProvisionAsync(ProvisionComputeRequest request, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        Id(request.WorkstreamId); Key(request.DesiredEnvironmentKey); Key(request.IdempotencyKey);
        var spec = request.Specification ?? throw new ArgumentException("A compute specification is required.", nameof(request));
        Identifier(spec.OperatingSystem); Identifier(spec.Architecture); Identifier(spec.TemplateId);
        if (spec.Resources is not { CpuCount: > 0, MemoryMiB: > 0, DiskMiB: > 0, GpuCount: >= 0 } ||
            spec.LifetimeSeconds < 0 || spec.Persistence is not ("ephemeral" or "persistent"))
            throw new ArgumentException("Positive resources, a lifetime and a supported persistence policy are required.", nameof(request));
        if (spec.Network is { } network)
        {
            var ports = network.PublishedPorts?.ToArray() ?? [];
            if (network.Mode is not ("none" or "private" or "outboundOnly" or "inbound") ||
                ports.Length > 64 || ports.Any(p => p is < 1 or > 65535) || ports.Distinct().Count() != ports.Length ||
                (network.Mode != "inbound" && (network.PublicEndpoint || ports.Length != 0)) ||
                (network.Mode == "none" && network.AllowOutbound) || (network.Mode == "outboundOnly" && !network.AllowOutbound) ||
                (network.PublicEndpoint && ports.Length == 0)) throw new ArgumentException("The network policy is invalid.", nameof(request));
            request = request with { Specification = spec with { Network = network with { PublishedPorts = ports } } };
        }
        return _platform.InvokeAsync<ProvisionComputeRequest, ComputeEnvironment>(ComputeCapabilities.Provision, request, token);
    }

    /// <summary>Reads application-selected workspace and template readiness. Does not grant authority or launch setup.</summary>
    public Task<ComputeDefaults> GetDefaultsAsync(CancellationToken token = default) =>
        _platform.InvokeAsync<object, ComputeDefaults>(ComputeCapabilities.Read, new { defaults = true }, token);

    public Task<ComputeDefaults> GetProjectDefaultsAsync(Guid projectId, CancellationToken token = default) =>
        _platform.InvokeAsync<object, ComputeDefaults>(ComputeCapabilities.Read, new { defaults = true, workstreamId = projectId }, token);

    public Task<ComputeEnvironment> ReadAsync(Guid environmentId, CancellationToken token = default)
    {
        Id(environmentId);
        return _platform.InvokeAsync<object, ComputeEnvironment>(ComputeCapabilities.Read, new { environmentId }, token);
    }

    public Task<ComputeOperation> ReadOperationAsync(Guid operationId, CancellationToken token = default)
    {
        Id(operationId);
        return _platform.InvokeAsync<object, ComputeOperation>(ComputeCapabilities.Read, new { operationId }, token);
    }

    public Task<ComputeEnvironmentPage> ListAsync(Guid workstreamId, Guid? afterId = null, int limit = 50,
        CancellationToken token = default)
    {
        Id(workstreamId); if (afterId is { } cursor) Id(cursor);
        if (limit is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(limit));
        object input = afterId is null ? new { workstreamId, limit } : new { workstreamId, afterId, limit };
        return _platform.InvokeAsync<object, ComputeEnvironmentPage>(ComputeCapabilities.List, input, token);
    }

    public Task<ComputeEnvironment> StartAsync(ComputeLifecycleRequest request, CancellationToken token = default) => Lifecycle(ComputeCapabilities.Start, request, token);
    public Task<ComputeEnvironment> StopAsync(ComputeLifecycleRequest request, CancellationToken token = default) => Lifecycle(ComputeCapabilities.Stop, request, token);
    public Task<ComputeEnvironment> RestartAsync(ComputeLifecycleRequest request, CancellationToken token = default) => Lifecycle(ComputeCapabilities.Restart, request, token);
    public Task<ComputeEnvironment> DestroyAsync(ComputeLifecycleRequest request, CancellationToken token = default) => Lifecycle(ComputeCapabilities.Destroy, request, token);

    public Task<ComputeOperation> ExecuteAsync(ExecuteComputeCommandRequest request, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        Scope(request.EnvironmentId, request.ExpectedGeneration, request.IdempotencyKey);
        var command = request.Command ?? throw new ArgumentException("A guest command is required.", nameof(request));
        Id(command.RequestId);
        if (command.TimeoutSeconds is < 1 or > 30 || command.MaximumOutputBytes is < 1 or > 8192 ||
            command.Arguments is null || command.Arguments.Count > 64 ||
            string.IsNullOrWhiteSpace(command.Executable) || string.IsNullOrWhiteSpace(command.WorkingDirectory))
            throw new ArgumentException("Commands require guest paths, at most 64 arguments, a 1â€“30 second timeout and 1â€“8192 output bytes.", nameof(request));
        // The broker validates paths against the actual guest OS, not the agent's OS.
        command = command with { Arguments = command.Arguments.ToArray() };
        if (command.Arguments.Any(a => a is null || a.Contains('\0')) ||
            command.Executable.Contains('\0') || command.WorkingDirectory.Contains('\0'))
            throw new ArgumentException("Command text must not contain null values or NUL characters.", nameof(request));
        object workload = request.NodeDependencies is null ? new { command } : new { command, nodeDependencies = request.NodeDependencies };
        return _platform.InvokeAsync<object, ComputeOperation>(ComputeCapabilities.Execute,
            new { request.EnvironmentId, request.ExpectedGeneration, request.IdempotencyKey, workload }, token);
    }

    /// <summary>Queues one archive export under existing compute execute authority. Does not execute a browser test.</summary>
    public Task<ComputeOperation> ExportEvidenceAsync(ExportComputeEvidenceRequest request, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        Scope(request.EnvironmentId, request.ExpectedGeneration, request.IdempotencyKey);
        var archive = request.Archive ?? throw new ArgumentException("An archive identity is required.");
        if (archive.SourceCommitSha is not { Length: 40 or 64 } || archive.SourceCommitSha.Any(c => !Uri.IsHexDigit(c) || char.IsUpper(c)) ||
            !EvidenceHash(archive.BuildSha256) || !EvidenceHash(archive.Sha256) || archive.ScriptSha256 is not null && !EvidenceHash(archive.ScriptSha256) ||
            archive.Length is < 1 or > 4 * 1024 * 1024) throw new ArgumentException("Invalid pinned evidence identity or length.");
        return _platform.InvokeAsync<object, ComputeOperation>(ComputeCapabilities.Execute,
            new { request.EnvironmentId, request.ExpectedGeneration, request.IdempotencyKey, workload = new { evidenceArchive = archive } }, token);
    }

    /// <summary>Downloads a completed retained archive in one callback, with bounded internal reads and final hash verification.
    /// Cancellation leaves the immutable artifact available; a later callback can restart this read without replaying tests.</summary>
    public async Task<byte[]> ReadEvidenceAsync(ComputeOperation operation, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        Id(operation.Id);
        var artifact = operation.Result?.Artifact;
        if (operation.Status != "Completed" || operation.Result?.ErrorCode is not null || artifact is null ||
            !EvidenceHash(artifact.Sha256) || artifact.Length is < 1 or > 4 * 1024 * 1024)
            throw new InvalidOperationException("A completed retained evidence artifact is required.");
        using var bytes = new MemoryStream(artifact.Length);
        using var hash = System.Security.Cryptography.IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA256);
        for (var offset = 0; offset < artifact.Length;)
        {
            var page = await _platform.InvokeAsync<object, ComputeEvidencePage>(ComputeCapabilities.Read,
                new { artifactId = operation.Id, offset }, token);
            var expected = Math.Min(64 * 1024, artifact.Length - offset);
            if (page.ArtifactId != operation.Id || page.Sha256 != artifact.Sha256 || page.Length != artifact.Length ||
                page.Offset != offset || page.Content is not { } content || content.Length != expected)
                throw new InvalidDataException("Evidence artifact read changed or was truncated.");
            hash.AppendData(content); await bytes.WriteAsync(content, token); offset += content.Length;
        }
        if (Convert.ToHexStringLower(hash.GetHashAndReset()) != artifact.Sha256)
            throw new InvalidDataException("Evidence artifact digest mismatch.");
        return bytes.ToArray();
    }

    private static bool EvidenceHash(string? value) => value is { Length: 64 } && value.All(c => c is >= 'a' and <= 'f' or >= '0' and <= '9');

    /// <summary>Publishes a guest HTTP port. Separate inbound and publish-port grants are required.
    /// Read the returned operation for a verified URL and expiry; the current provider returns local-machine links.</summary>
    public Task<ComputeOperation> PublishPortAsync(PublishComputePortRequest request, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        Scope(request.EnvironmentId, request.ExpectedGeneration, request.IdempotencyKey);
        if (request.GuestPort is < 1024 or > 65535) throw new ArgumentOutOfRangeException(nameof(request.GuestPort));
        return _platform.InvokeAsync<object, ComputeOperation>(ComputeCapabilities.PublishPort,
            new { request.EnvironmentId, request.ExpectedGeneration, request.IdempotencyKey, workload = new { publishPort = request.GuestPort } }, token);
    }

    private Task<ComputeEnvironment> Lifecycle(string capability, ComputeLifecycleRequest request, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(request);
        Scope(request.EnvironmentId, request.ExpectedGeneration, request.IdempotencyKey);
        return _platform.InvokeAsync<ComputeLifecycleRequest, ComputeEnvironment>(capability, request, token);
    }

    private static void Scope(Guid id, long generation, string key)
    {
        Id(id); Key(key); if (generation < 1) throw new ArgumentOutOfRangeException(nameof(generation));
    }
    private static void Id(Guid id) { if (id == Guid.Empty) throw new ArgumentException("A nonempty resource ID is required."); }
    private static void Key(string key)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length > 128 || key.Any(char.IsControl))
            throw new ArgumentException("Use a stable, nonempty key of at most 128 characters.");
    }
    private static void Identifier(string value)
    {
        if (value is not { Length: > 0 and <= 64 } || value[0] is < 'a' or > 'z' ||
            value.Any(c => c is not (>= 'a' and <= 'z' or >= '0' and <= '9' or '-')))
            throw new ArgumentException("Use a platform OS, architecture or template identifier.");
    }
}
