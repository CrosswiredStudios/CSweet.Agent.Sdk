using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CSweet.Agent.SDK.Compute;

namespace CSweet.Agent.SDK;

public sealed partial class PlatformComputeClient
{
    /// <summary>Stages immutable input parts without compute effects, then submits one resumable transfer.
    /// Replaying the same bytes/key resumes staging and the same operation; it never replays browser tests.</summary>
    public async Task<ComputeOperation> UploadBuildAsync(Guid environmentId, long expectedGeneration,
        string sourceCommitSha, ReadOnlyMemory<byte> bytes, string idempotencyKey, CancellationToken token = default)
    {
        Scope(environmentId, expectedGeneration, idempotencyKey);
        var input = await StageBuildAsync(sourceCommitSha, bytes, token);
        return await _platform.InvokeAsync<object, ComputeOperation>(ComputeCapabilities.Execute,
            new { environmentId, expectedGeneration, idempotencyKey, workload = new { buildArchive = input } }, token);
    }

    /// <summary>Retains hash-bound parts for one build, without changing a VM generation or running compute.</summary>
    public async Task<ComputeBuildArchiveInput> StageBuildAsync(string sourceCommitSha, ReadOnlyMemory<byte> bytes, CancellationToken token = default)
    {
        if (sourceCommitSha is not { Length: 40 or 64 } || sourceCommitSha.Any(c => !"0123456789abcdef".Contains(c)) ||
            bytes.Length is < 1 or > 16 * 1024 * 1024) throw new ArgumentException("An exact source and a static build of 1 byte through 16 MiB are required.");
        var build = Convert.ToHexStringLower(SHA256.HashData(bytes.Span));
        var hashes = new List<string>();
        for (var offset = 0; offset < bytes.Length; offset += 65536)
        {
            token.ThrowIfCancellationRequested();
            var content = bytes.Slice(offset, Math.Min(65536, bytes.Length - offset)).ToArray();
            var part = new ComputeBuildInputPart(sourceCommitSha, build, bytes.Length, offset, content);
            var hash = Convert.ToHexStringLower(SHA256.HashData(content)); hashes.Add(hash);
            var key = BuildPartKey(sourceCommitSha, build, offset);
            var prior = await _platform.ReadOperatingStateAsync<ComputeBuildInputPart>(key, token);
            if (prior is null)
            {
                try
                {
                    await _platform.WriteOperatingStateAsync(new(key, "compute.build-input-part.v1", 1, "Active",
                        new Dictionary<string, string>(), [], hash, [], Guid.NewGuid(),
                        JsonSerializer.SerializeToElement(part, new JsonSerializerOptions(JsonSerializerDefaults.Web)), null, key), token);
                }
                catch (PlatformCapabilityException error) when (error.Code == PlatformCapabilityErrorCode.Conflict) { }
                prior = await _platform.ReadOperatingStateAsync<ComputeBuildInputPart>(key, token);
            }
            if (prior?.SchemaId != "compute.build-input-part.v1" || prior.Payload is not { } saved ||
                saved.SourceCommitSha != sourceCommitSha || saved.BuildSha256 != build || saved.Length != bytes.Length ||
                saved.Offset != offset || saved.Content is null || !saved.Content.AsSpan().SequenceEqual(content))
                throw new InvalidDataException("Staged build input changed or was not retained.");
        }
        return new(sourceCommitSha, build, bytes.Length, hashes);
    }

    internal static string BuildPartKey(string source, string build, int offset) => "compute-build:" +
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(source + ":" + build))) + ":" + offset;
}
