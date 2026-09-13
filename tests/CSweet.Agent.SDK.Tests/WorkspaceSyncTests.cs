using System.IO.Compression;
using System.Text;

namespace CSweet.Agent.SDK.Tests;

public sealed class WorkspaceSyncTests
{
    [Fact]
    public async Task Materialization_preserves_edits_and_uploaded_source_recovers_after_restart()
    {
        var workspace = new GitWorkspaceResult(Guid.NewGuid(), Guid.NewGuid(), "/workspace/remote-only", Guid.NewGuid(), "InternalGit", "PullRequest", new string('a', 40), "Ready", false);
        var source = Zip("app.js", "original");
        var runtime = new AgentTestRuntime().RegisterCapability<GitWorkspaceSyncRequest, GitWorkspaceSyncResult>(GitWorkspaceCapabilities.Sync, (request, _) =>
        {
            Assert.Equal(workspace.WorkspaceId, request.WorkspaceId); Assert.Equal(1, request.AssignmentRevision);
            if (request.Direction == "push") { source = request.Archive!; return Task.FromResult(new GitWorkspaceSyncResult()); }
            Assert.Null(request.Archive); return Task.FromResult(new GitWorkspaceSyncResult(source));
        });
        var git = runtime.CreateContext().Platform.Git;
        var path = PlatformGitWorkspaceClient.LocalWorkspacePath(workspace.WorkspaceId);
        try
        {
            var local = await git.MaterializeAsync(workspace, 1);
            Assert.Equal("original", await File.ReadAllTextAsync(Path.Combine(local.Path, "app.js")));
            await File.WriteAllTextAsync(Path.Combine(local.Path, "app.js"), "edited and tested");
            await git.MaterializeAsync(workspace, 1);
            Assert.Equal("edited and tested", await File.ReadAllTextAsync(Path.Combine(local.Path, "app.js")));
            await git.UploadAsync(local, 1);
            using (var zip = new ZipArchive(new MemoryStream(source))) Assert.Equal("app.js", Assert.Single(zip.Entries).FullName);
            Directory.Delete(path, recursive: true);
            var restored = await git.MaterializeAsync(workspace, 1);
            Assert.Equal("edited and tested", await File.ReadAllTextAsync(Path.Combine(restored.Path, "app.js")));
        }
        finally { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); }
    }

    [Theory]
    [InlineData("../escape")]
    [InlineData("/absolute")]
    [InlineData(".git/config")]
    [InlineData("C:/escape")]
    public async Task Materialization_rejects_unsafe_archive_paths(string entry)
    {
        var workspace = new GitWorkspaceResult(Guid.NewGuid(), Guid.NewGuid(), "ignored", Guid.NewGuid(), "InternalGit", "PullRequest", "base", "Ready", false);
        var runtime = new AgentTestRuntime().RegisterCapability<GitWorkspaceSyncRequest, GitWorkspaceSyncResult>(GitWorkspaceCapabilities.Sync,
            (_, _) => Task.FromResult(new GitWorkspaceSyncResult(Zip(entry, "untrusted"))));
        await Assert.ThrowsAsync<InvalidDataException>(() => runtime.CreateContext().Platform.Git.MaterializeAsync(workspace, 1));
        Assert.False(Directory.Exists(PlatformGitWorkspaceClient.LocalWorkspacePath(workspace.WorkspaceId)));
    }

    [Fact]
    public async Task Upload_rejects_caller_selected_paths()
    {
        var workspace = new GitWorkspaceResult(Guid.NewGuid(), Guid.NewGuid(), Path.GetTempPath(), Guid.NewGuid(), "InternalGit", "PullRequest", "base", "Ready", false);
        await Assert.ThrowsAsync<ArgumentException>(() => new AgentTestRuntime().CreateContext().Platform.Git.UploadAsync(workspace, 1));
    }

    private static byte[] Zip(string name, string text)
    {
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, true))
        using (var writer = new StreamWriter(zip.CreateEntry(name).Open(), Encoding.UTF8)) writer.Write(text);
        return stream.ToArray();
    }
}
