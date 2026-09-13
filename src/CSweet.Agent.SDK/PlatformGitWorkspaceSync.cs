using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace CSweet.Agent.SDK;

public sealed record GitWorkspaceSyncRequest(Guid WorkspaceId, long AssignmentRevision, string Direction,
    string IdempotencyKey, byte[]? Archive = null);
public sealed record GitWorkspaceSyncResult(byte[]? Archive = null);

public sealed partial class PlatformGitWorkspaceClient
{
    public const string SyncCapability = "git.workspace.sync.v1";
    public const int MaximumSnapshotBytes = 512 * 1024;
    public const int MaximumSnapshotContentBytes = 16 * 1024 * 1024;
    public static string LocalWorkspaceRoot => Path.Combine(Path.GetTempPath(), "csweet-workspaces");
    public static string LocalWorkspacePath(Guid workspaceId) => Path.Combine(LocalWorkspaceRoot, workspaceId.ToString("N"));

    /// <summary>Download the authorized snapshot into this isolated runtime's writable temporary storage.
    /// Existing edits are preserved. A runtime restart can recover from Core's retained snapshot.</summary>
    public async Task<GitWorkspaceResult> MaterializeAsync(GitWorkspaceResult workspace, long assignmentRevision, CancellationToken ct = default)
    {
        if (workspace.WorkspaceId == Guid.Empty || assignmentRevision < 1) throw new ArgumentException("A current workspace is required.");
        var result = await InvokeAsync<GitWorkspaceSyncRequest, GitWorkspaceSyncResult>(SyncCapability,
            new(workspace.WorkspaceId, assignmentRevision, "pull", $"workspace:{workspace.WorkspaceId:N}:pull:{assignmentRevision}"), ct);
        var path = LocalWorkspacePath(workspace.WorkspaceId);
        var receipt = Path.Combine(path, ".csweet", "runtime-workspace.txt");
        var identity = $"{workspace.WorkspaceId:D}:{assignmentRevision}";
        EnsureNoLinks(path);
        if (Directory.Exists(path))
        {
            EnsureNoLinks(receipt);
            if (!File.Exists(receipt) || await File.ReadAllTextAsync(receipt, ct) != identity)
                throw new InvalidOperationException("The local workspace identity does not match the retained assignment.");
            return workspace with { Path = path };
        }
        var archive = result.Archive ?? throw new InvalidDataException("The platform returned no source snapshot.");
        if (archive.Length > MaximumSnapshotBytes) throw new InvalidDataException("The source snapshot exceeds the 512 KiB transfer limit.");
        Directory.CreateDirectory(LocalWorkspaceRoot);
        var staging = Path.Combine(LocalWorkspaceRoot, ".prepare-" + Guid.NewGuid().ToString("N"));
        try
        {
            await ExtractSnapshotAsync(archive, staging, ct);
            Directory.CreateDirectory(Path.Combine(staging, ".csweet"));
            await File.WriteAllTextAsync(Path.Combine(staging, ".csweet", "runtime-workspace.txt"), identity, ct);
            Directory.Move(staging, path);
        }
        finally { if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true); }
        return workspace with { Path = path };
    }

    /// <summary>Send edited source back to Core before inspection or publication. No Git credentials or host mounts.</summary>
    public async Task UploadAsync(GitWorkspaceResult workspace, long assignmentRevision, CancellationToken ct = default)
    {
        var path = LocalWorkspacePath(workspace.WorkspaceId);
        if (workspace.WorkspaceId == Guid.Empty || assignmentRevision < 1 || Path.GetFullPath(workspace.Path) != path)
            throw new ArgumentException("Only the SDK-materialized workspace can be uploaded.");
        EnsureNoLinks(path);
        var archive = await CreateSnapshotAsync(path, ct);
        var digest = Convert.ToHexStringLower(SHA256.HashData(archive));
        await InvokeAsync<GitWorkspaceSyncRequest, GitWorkspaceSyncResult>(SyncCapability,
            new(workspace.WorkspaceId, assignmentRevision, "push", $"workspace:{workspace.WorkspaceId:N}:{digest}", archive), ct);
    }

    internal static async Task ExtractSnapshotAsync(byte[] archive, string root, CancellationToken ct)
    {
        if (archive.Length > MaximumSnapshotBytes) throw new InvalidDataException("Snapshot transfer is too large.");
        EnsureNoLinks(root); Directory.CreateDirectory(root);
        using var zip = new ZipArchive(new MemoryStream(archive), ZipArchiveMode.Read);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long total = 0;
        foreach (var entry in zip.Entries)
        {
            var relative = entry.FullName;
            var parts = relative.TrimEnd('/').Split('/');
            if (parts.Any(p => p is "" or "." or ".." || p.Contains(':') || p.Contains('\\') || p.Any(char.IsControl) || p.TrimEnd(' ', '.') != p || p.Equals(".git", StringComparison.OrdinalIgnoreCase)) ||
                relative.Length > 512 || !names.Add(relative.TrimEnd('/')) || names.Count > 4096 ||
                ((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000 || (entry.ExternalAttributes & (int)FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("The source snapshot contains an unsafe path or link.");
            total = checked(total + entry.Length);
            if (total > MaximumSnapshotContentBytes) throw new InvalidDataException("The source snapshot exceeds 16 MiB of content.");
            var target = Path.GetFullPath(Path.Combine(root, relative));
            if (!target.StartsWith(Path.GetFullPath(root) + Path.DirectorySeparatorChar, StringComparison.Ordinal)) throw new InvalidDataException("Invalid snapshot path.");
            if (relative.EndsWith('/')) { Directory.CreateDirectory(target); continue; }
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            await using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            await using var input = entry.Open();
            await input.CopyToAsync(output, ct);
            if (output.Length != entry.Length) throw new InvalidDataException("Snapshot size mismatch.");
        }
    }

    internal static async Task<byte[]> CreateSnapshotAsync(string root, CancellationToken ct)
    {
        EnsureNoLinks(root);
        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            long total = 0; var count = 0;
            foreach (var path in Directory.EnumerateFiles(root, "*", new EnumerationOptions { RecurseSubdirectories = true,
                AttributesToSkip = FileAttributes.ReparsePoint, IgnoreInaccessible = false }).Order(StringComparer.Ordinal))
            {
                var relative = Path.GetRelativePath(root, path).Replace('\\', '/');
                if (relative.Split('/').Any(p => p.Equals(".git", StringComparison.OrdinalIgnoreCase) || p.Equals(".csweet", StringComparison.OrdinalIgnoreCase))) continue;
                EnsureNoLinks(path); total = checked(total + new FileInfo(path).Length);
                if (++count > 4096 || total > MaximumSnapshotContentBytes) throw new InvalidDataException("Source exceeds the workspace content limit.");
                var entry = zip.CreateEntry(relative, CompressionLevel.Optimal);
                entry.LastWriteTime = new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);
                await using var input = File.OpenRead(path); await using var stream = entry.Open();
                await input.CopyToAsync(stream, ct);
            }
        }
        if (output.Length > MaximumSnapshotBytes) throw new InvalidDataException("The source snapshot exceeds the 512 KiB transfer limit.");
        return output.ToArray();
    }

    private static void EnsureNoLinks(string path)
    {
        for (var current = Path.GetFullPath(path); !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
        {
            try { if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("The workspace path redirects elsewhere."); }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
    }
}
