using System.Text.RegularExpressions;

namespace CSweet.Agent.SDK;

/// <summary>Who a local commit is attributed to.</summary>
public sealed record GitWorkspaceCommitRequest(string Message, string AuthorName, string AuthorEmail);

/// <summary>A git command for an assigned workspace failed. <see cref="Detail"/> is git's own
/// message, including the platform's reason when a push was refused.</summary>
public sealed class GitWorkspaceCommandException(string message, int exitCode, string detail) : Exception(message)
{
    public int ExitCode { get; } = exitCode;
    public string Detail { get; } = detail;
}

public sealed partial class PlatformGitWorkspaceClient
{
    private static readonly Regex BranchPattern = new(@"\A[A-Za-z0-9][A-Za-z0-9._/-]{0,199}\z", RegexOptions.CultureInvariant);
    private static readonly Regex CommitPattern = new(@"\A([0-9a-f]{40}|[0-9a-f]{64})\z", RegexOptions.CultureInvariant);
    // Never committed: the SDK's receipt and dependency folders every developer restores for themselves.
    private static readonly string[] LocalExcludes = [".csweet/", "node_modules/", ".venv/"];

    /// <summary>
    /// Clone the assigned workspace with git into this runtime's writable storage and check out its
    /// work branch. The repository, branch and base commit come from the platform; none can be chosen
    /// here. The runtime holds no Git credential: every exchange is authorized by the platform for this
    /// assignment. An existing local clone for the same assignment is kept as it is, edits included.
    /// </summary>
    public async Task<GitWorkspaceResult> CloneAsync(GitWorkspaceResult workspace, long assignmentRevision, CancellationToken ct = default)
    {
        var (branch, remote) = RequireGitRemote(workspace, assignmentRevision);
        if (!CommitPattern.IsMatch(workspace.BaseCommitSha))
            throw new ArgumentException("The workspace has no valid base commit.");
        var path = LocalWorkspacePath(workspace.WorkspaceId);
        var receipt = Path.Combine(path, ".csweet", "runtime-workspace.txt");
        var identity = $"{workspace.WorkspaceId:D}:{assignmentRevision}";
        if (Directory.Exists(path))
        {
            if (!File.Exists(receipt) || await File.ReadAllTextAsync(receipt, ct) != identity ||
                !Directory.Exists(Path.Combine(path, ".git")))
                throw new InvalidOperationException("The local workspace identity does not match the retained assignment.");
            // The forwarder port changes when the agent process restarts.
            await RequireGitAsync(path, ["remote", "set-url", "origin", remote.RemoteUrl], null, "configure the workspace remote", ct);
            return workspace with { Path = path };
        }
        Directory.CreateDirectory(LocalWorkspaceRoot);
        var staging = Path.Combine(LocalWorkspaceRoot, ".clone-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        try
        {
            await RequireGitAsync(staging, ["init", "--quiet", $"--initial-branch={branch}"], null, "create the workspace", ct);
            await RequireGitAsync(staging, ["remote", "add", "origin", remote.RemoteUrl], null, "configure the workspace remote", ct);
            await RequireGitAsync(staging, ["fetch", "--quiet", "--no-tags", "origin"], remote.Environment, "fetch the repository", ct);
            // Work pushed earlier for this assignment wins over the base commit, so a new runtime resumes it.
            var pushed = await GitProcess.RunAsync(staging,
                ["rev-parse", "--verify", "--quiet", $"refs/remotes/origin/{branch}^{{commit}}"], null, ct);
            await RequireGitAsync(staging,
                ["checkout", "--quiet", "-B", branch, pushed.ExitCode == 0 ? $"refs/remotes/origin/{branch}" : workspace.BaseCommitSha],
                null, "check out the work branch", ct);
            await File.AppendAllLinesAsync(Path.Combine(staging, ".git", "info", "exclude"), LocalExcludes, ct);
            Directory.CreateDirectory(Path.Combine(staging, ".csweet"));
            await File.WriteAllTextAsync(Path.Combine(staging, ".csweet", "runtime-workspace.txt"), identity, ct);
            Directory.Move(staging, path);
        }
        finally
        {
            if (Directory.Exists(staging)) DeleteDirectory(staging);
        }
        return workspace with { Path = path };
    }

    /// <summary>Fetch the repository's current branches into the local clone without changing the working tree.</summary>
    public async Task FetchAsync(GitWorkspaceResult workspace, long assignmentRevision, CancellationToken ct = default)
    {
        var (_, remote) = RequireGitRemote(workspace, assignmentRevision);
        var path = RequireLocalClone(workspace);
        await RequireGitAsync(path, ["remote", "set-url", "origin", remote.RemoteUrl], null, "configure the workspace remote", ct);
        await RequireGitAsync(path, ["fetch", "--quiet", "--no-tags", "origin"], remote.Environment, "fetch the repository", ct);
    }

    /// <summary>
    /// Commit every change in the local clone to the work branch. Honors the repository's
    /// <c>.gitignore</c>. Returns the new commit, or <see langword="null"/> when nothing changed.
    /// This is local only; call <see cref="PushAsync"/> to send it to the platform.
    /// </summary>
    public async Task<string?> CommitAsync(GitWorkspaceResult workspace, GitWorkspaceCommitRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Message) || request.Message.Length > 4000 || request.Message.Contains('\0'))
            throw new ArgumentException("A commit message of at most 4,000 characters is required.");
        if (!IsIdentityPart(request.AuthorName) || !IsIdentityPart(request.AuthorEmail))
            throw new ArgumentException("A commit author name and email without angle brackets or line breaks are required.");
        var path = RequireLocalClone(workspace);
        await RequireGitAsync(path, ["add", "--all"], null, "stage the changes", ct);
        var staged = await GitProcess.RunAsync(path, ["diff", "--cached", "--quiet"], null, ct);
        if (staged.ExitCode == 0) return null;
        await RequireGitAsync(path,
            ["-c", $"user.name={request.AuthorName}", "-c", $"user.email={request.AuthorEmail}", "commit", "--quiet", "-m", request.Message],
            null, "commit the changes", ct);
        return await HeadAsync(path, ct);
    }

    /// <summary>
    /// Push the local work branch to the platform and return the pushed commit. The platform accepts
    /// only this assignment's branch and applies its push rules; a refusal is reported with the
    /// platform's reason in <see cref="GitWorkspaceCommandException.Detail"/>.
    /// </summary>
    public async Task<string> PushAsync(GitWorkspaceResult workspace, long assignmentRevision, CancellationToken ct = default)
    {
        var (branch, remote) = RequireGitRemote(workspace, assignmentRevision);
        var path = RequireLocalClone(workspace);
        await RequireGitAsync(path, ["remote", "set-url", "origin", remote.RemoteUrl], null, "configure the workspace remote", ct);
        await RequireGitAsync(path, ["push", "--quiet", "origin", $"HEAD:refs/heads/{branch}"], remote.Environment, "push the work branch", ct);
        return await HeadAsync(path, ct);
    }

    private (string Branch, AgentGitRemoteAccess Remote) RequireGitRemote(GitWorkspaceResult workspace, long assignmentRevision)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        if (workspace.WorkspaceId == Guid.Empty || assignmentRevision < 1) throw new ArgumentException("A current workspace is required.");
        if (string.IsNullOrEmpty(workspace.BranchName))
            throw new NotSupportedException("This platform did not name the workspace's work branch. Use MaterializeAsync and UploadAsync.");
        if (!BranchPattern.IsMatch(workspace.BranchName) || workspace.BranchName.Contains("..", StringComparison.Ordinal) ||
            workspace.BranchName.EndsWith('/') || workspace.BranchName.EndsWith(".lock", StringComparison.Ordinal))
            throw new ArgumentException("The workspace's work branch name is not valid.");
        var remote = (_tools as IAgentGitRemote)?.OpenGitRemote(workspace.WorkspaceId, assignmentRevision)
            ?? throw new NotSupportedException("This runtime has no Git route. Use MaterializeAsync and UploadAsync.");
        return (workspace.BranchName, remote);
    }

    private static string RequireLocalClone(GitWorkspaceResult workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        var path = LocalWorkspacePath(workspace.WorkspaceId);
        if (workspace.WorkspaceId == Guid.Empty || Path.GetFullPath(workspace.Path) != path ||
            !Directory.Exists(Path.Combine(path, ".git")))
            throw new ArgumentException("Only the workspace cloned by CloneAsync can be used.");
        return path;
    }

    private static bool IsIdentityPart(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= 200 && value.IndexOfAny(['<', '>', '\r', '\n', '\0']) < 0;

    private static async Task<string> HeadAsync(string path, CancellationToken ct) =>
        (await RequireGitAsync(path, ["rev-parse", "HEAD"], null, "read the current commit", ct)).Output.Trim();

    private static async Task<GitProcessResult> RequireGitAsync(string path, IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string>? environment, string action, CancellationToken ct)
    {
        var result = await GitProcess.RunAsync(path, arguments, environment, ct);
        if (result.ExitCode != 0)
            throw new GitWorkspaceCommandException($"git could not {action}.", result.ExitCode, result.Error.Trim());
        return result;
    }

    private static void DeleteDirectory(string path)
    {
        // Git marks object files read-only, which blocks a plain recursive delete on Windows.
        foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            File.SetAttributes(file, FileAttributes.Normal);
        Directory.Delete(path, recursive: true);
    }
}
