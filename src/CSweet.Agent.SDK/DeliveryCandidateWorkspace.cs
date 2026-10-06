using System.IO.Compression;
using System.Security.Cryptography;
using CSweet.WorkManagement.Contracts;

namespace CSweet.Agent.SDK;

/// <summary>Materializes only broker-authorized immutable snapshots in an isolated local review directory.</summary>
public sealed class DeliveryCandidateWorkspace : IAsyncDisposable
{
    private readonly Dictionary<string, string> _sourceHashes = new(StringComparer.Ordinal);
    public string Path { get; }
    private DeliveryCandidateWorkspace(string path) => Path = path;
    public static async Task<DeliveryCandidateWorkspace> MaterializeAsync(WorkExecutionAssignmentV2 assignment,
        AgentRuntimeContext context, CancellationToken ct)
    {
        if (assignment.DeliveryPlanId is not { } plan || assignment.Candidate is not { } candidate)
            throw new InvalidOperationException("An exact delivery candidate is required.");
        var root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "csweet-delivery-reviews", assignment.AttemptId.ToString("N"), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root); var workspace = new DeliveryCandidateWorkspace(root);
        try
        {
            foreach (var repository in candidate.Repositories)
            {
                var evidence = await context.Platform.Work.ReadDeliveryEvidenceAsync(new(plan, assignment.ExecutionId, repository.RepositoryId), ct);
                if (evidence.Candidate.Digest != candidate.Digest || evidence.CommitSha != repository.CandidateCommitSha || evidence.Archive is not { Length: > 0 and <= 104857600 } archive ||
                    Convert.ToHexStringLower(SHA256.HashData(archive)) != evidence.ArchiveSha256)
                    throw new InvalidOperationException("The broker snapshot does not match the exact candidate archive and commit.");
                var repositoryRoot = System.IO.Path.Combine(root, "repositories", repository.RepositoryId.ToString("N"));
                Directory.CreateDirectory(repositoryRoot);
                using var zip = new ZipArchive(new MemoryStream(archive), ZipArchiveMode.Read);
                if (zip.Entries.Count > 50000 || zip.Entries.Sum(x => x.Length) > 536870912)
                    throw new InvalidDataException("The candidate snapshot exceeds the bounded review limit.");
                var seen = new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
                foreach (var entry in zip.Entries)
                {
                    ct.ThrowIfCancellationRequested(); var name = entry.FullName.Replace('\\', '/');
                    if (name.StartsWith('/') || name.Split('/').Any(x => x is "." or ".." or ".git") || name.Contains(':') ||
                        ((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000) throw new InvalidDataException("The candidate archive contains an unsafe path or symbolic link.");
                    var file = System.IO.Path.GetFullPath(System.IO.Path.Combine(repositoryRoot, name));
                    if (!file.StartsWith(repositoryRoot + System.IO.Path.DirectorySeparatorChar, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                        throw new InvalidDataException("The candidate archive escapes its assigned directory.");
                    if (name.EndsWith('/')) { Directory.CreateDirectory(file); continue; }
                    if (!seen.Add(file)) throw new InvalidDataException("The candidate archive contains duplicate paths.");
                    Directory.CreateDirectory(System.IO.Path.GetDirectoryName(file)!);
                    await using (var source = entry.Open()) await using (var output = File.Create(file)) await source.CopyToAsync(output, ct);
                    workspace._sourceHashes.Add(file, Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(file, ct))));
                }
            }
            var documentEvidence = await context.Platform.Work.ReadDeliveryEvidenceAsync(new(plan, assignment.ExecutionId), ct);
            if (documentEvidence.Candidate.Digest != candidate.Digest) throw new InvalidOperationException("The document candidate changed.");
            foreach (var document in documentEvidence.Documents)
            {
                if (!candidate.Documents.Any(x => x.ArtifactId == document.ArtifactId && x.RevisionId == document.RevisionId && x.Sha256 == document.Sha256) ||
                    Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(document.Content))) != document.Sha256)
                    throw new InvalidOperationException("The delivered candidate document identity changed.");
                var file = System.IO.Path.Combine(root, "documents", document.RevisionId.ToString("N") + ".md");
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(file)!); await File.WriteAllTextAsync(file, document.Content, ct);
                workspace._sourceHashes.Add(file, document.Sha256);
            }
            return workspace;
        }
        catch { await workspace.DisposeAsync(); throw; }
    }
    public async Task VerifySourceUnchangedAsync(CancellationToken ct)
    {
        foreach (var source in _sourceHashes)
            if (!File.Exists(source.Key) || Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(source.Key, ct))) != source.Value)
                throw new InvalidOperationException("The reviewer modified candidate source; no validation result may be accepted.");
    }
    public async Task<IReadOnlyList<DeliveryReviewSourceFile>> ReadSourceForReviewAsync(CancellationToken ct)
    {
        var files = new List<DeliveryReviewSourceFile>(); var characters = 0;
        foreach (var entry in _sourceHashes.OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            ct.ThrowIfCancellationRequested();
            var bytes = await File.ReadAllBytesAsync(entry.Key, ct);
            var binary = bytes.Contains((byte)0);
            var content = binary ? null : System.Text.Encoding.UTF8.GetString(bytes);
            characters += content?.Length ?? 0;
            if (characters > 1500000 || files.Count >= 5000)
                throw new InvalidOperationException("The candidate exceeds bounded source review capacity; assign a reviewer with an appropriate source inspection toolchain.");
            files.Add(new(System.IO.Path.GetRelativePath(Path, entry.Key).Replace('\\', '/'), entry.Value, bytes.Length, content));
        }
        return files;
    }
    public ValueTask DisposeAsync()
    {
        // Path is generated by this class, never taken from an agent or repository.
        if (Directory.Exists(Path)) Directory.Delete(Path, true);
        return ValueTask.CompletedTask;
    }
}
public sealed record DeliveryReviewSourceFile(string Path, string Sha256, long Bytes, string? Content);
