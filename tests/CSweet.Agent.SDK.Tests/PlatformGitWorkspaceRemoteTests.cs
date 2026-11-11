using System.Runtime.CompilerServices;
using System.Text.Json;

namespace CSweet.Agent.SDK.Tests;

/// <summary>Runs the real git program against a local bare repository standing in for the platform.</summary>
public sealed class PlatformGitWorkspaceRemoteTests
{
    private const string Branch = "work/task-1";
    private static readonly GitWorkspaceCommitRequest Commit = new("feat(task): add b", "Adrian", "adrian@agents.csweet.invalid");

    [Fact]
    public async Task Clone_commit_and_push_deliver_to_the_work_branch_and_a_new_clone_resumes_it()
    {
        await using var fixture = await Fixture.CreateAsync();
        var git = fixture.Client();

        var local = await git.CloneAsync(fixture.Workspace, 7);

        Assert.Equal(PlatformGitWorkspaceClient.LocalWorkspacePath(fixture.Workspace.WorkspaceId), local.Path);
        Assert.Equal("a", (await File.ReadAllTextAsync(Path.Combine(local.Path, "a.txt"))).Trim());
        Assert.Equal(Branch, await fixture.GitAsync(local.Path, "rev-parse", "--abbrev-ref", "HEAD"));
        Assert.Equal(fixture.Workspace.BaseCommitSha, await fixture.GitAsync(local.Path, "rev-parse", "HEAD"));
        Assert.Null(await git.CommitAsync(local, Commit));

        await File.WriteAllTextAsync(Path.Combine(local.Path, "b.txt"), "b");
        Directory.CreateDirectory(Path.Combine(local.Path, "node_modules", "left-pad"));
        await File.WriteAllTextAsync(Path.Combine(local.Path, "node_modules", "left-pad", "index.js"), "x");
        var commit = await git.CommitAsync(local, Commit);
        var pushed = await git.PushAsync(local, 7);

        Assert.NotNull(commit);
        Assert.Equal(commit, pushed);
        Assert.Null(await git.CommitAsync(local, Commit));
        Assert.Equal(commit, await fixture.GitAsync(fixture.Remote, "rev-parse", $"refs/heads/{Branch}"));
        Assert.Equal("Adrian <adrian@agents.csweet.invalid>", await fixture.GitAsync(fixture.Remote, "log", "-1", "--format=%an <%ae>", Branch));
        var files = (await fixture.GitAsync(fixture.Remote, "ls-tree", "-r", "--name-only", Branch)).Split('\n');
        Assert.Equal(new[] { "a.txt", "b.txt" }, files.Select(file => file.Trim()).Order().ToArray());

        // A replacement runtime starts with an empty disk and must find the pushed work.
        Fixture.Delete(local.Path);
        var resumed = await git.CloneAsync(fixture.Workspace, 7);
        Assert.Equal("b", await File.ReadAllTextAsync(Path.Combine(resumed.Path, "b.txt")));
        Assert.Equal(commit, await fixture.GitAsync(resumed.Path, "rev-parse", "HEAD"));
    }

    [Fact]
    public async Task Clone_returns_resume_state_and_a_checkpoint_commits_pushes_and_is_recorded_by_the_platform()
    {
        await using var fixture = await Fixture.CreateAsync();
        var git = fixture.Client();

        var local = await git.CloneAsync(fixture.Workspace, 7);

        Assert.Equal(4096, Assert.Single(local.ResourceProfiles).MemoryMb);
        Assert.Equal(new string('e', 64), local.ResumeSummary!.SourceFingerprint);
        Assert.Equal("resume", Assert.Single(fixture.Platform.Syncs).Direction);

        await File.WriteAllTextAsync(Path.Combine(local.Path, "wip.txt"), "half done");
        var summary = new WorkspaceResumeSummary(new string('d', 64), [new("npm test", false, 1)]);
        var receipt = await git.PushCheckpointAsync(local, 7, Commit, default, "build", new string('c', 64), summary);

        var tip = await fixture.GitAsync(fixture.Remote, "rev-parse", $"refs/heads/{Branch}");
        Assert.Equal(new WorkspaceCheckpointReceipt(fixture.Workspace.WorkspaceId, 7, tip), receipt);
        Assert.Contains("wip.txt", await fixture.GitAsync(fixture.Remote, "ls-tree", "-r", "--name-only", Branch));
        var recorded = fixture.Platform.Syncs[^1];
        Assert.Equal(("checkpoint", tip, "build", new string('c', 64)),
            (recorded.Direction, recorded.PushedCommitSha, recorded.CommandClass, recorded.InputFingerprint));
        Assert.Equal(summary.SourceFingerprint, recorded.ResumeSummary!.SourceFingerprint);

        // Nothing changed since: the checkpoint still names the pushed tip and makes no new commit.
        Assert.Equal(tip, (await git.PushCheckpointAsync(local, 7, Commit)).Digest);
    }

    [Fact]
    public async Task The_snapshot_calls_use_git_for_a_workspace_the_platform_gave_a_work_branch()
    {
        await using var fixture = await Fixture.CreateAsync();
        var git = fixture.Client();

        var local = await git.MaterializeAsync(fixture.Workspace, 7);
        Assert.True(Directory.Exists(Path.Combine(local.Path, ".git")));
        Assert.Single(local.ResourceProfiles);

        await File.WriteAllTextAsync(Path.Combine(local.Path, "b.txt"), "b");
        await git.UploadAsync(local, 7);
        var tip = await fixture.GitAsync(fixture.Remote, "rev-parse", $"refs/heads/{Branch}");
        Assert.NotEqual(fixture.Workspace.BaseCommitSha, tip);
        Assert.Equal("C-Sweet Agent <agent@agents.csweet.invalid>", await fixture.GitAsync(fixture.Remote, "log", "-1", "--format=%an <%ae>", Branch));

        var receipt = await git.UploadCheckpointAsync(local, 7, default, "build", new string('c', 64));
        Assert.Equal(tip, receipt.Digest);

        var inspection = await git.InspectAsync(new(local.WorkspaceId, 7));
        Assert.Equal("Modified", inspection.Status);
        Assert.Equal(tip, Assert.Single(fixture.Platform.Inspections).PushedCommitSha);

        var publication = await git.PublishAsync(new(local.WorkspaceId, 7, "Implement ticket", "Ticket", "Evidence", "event:publish",
            [new GitValidationResult("npm test", true, 0)]));
        Assert.Equal(tip, publication.CommitSha);
        Assert.Equal(tip, Assert.Single(fixture.Platform.Publications).PushedCommitSha);

        // No archive ever travels for this workspace.
        Assert.All(fixture.Platform.Syncs, sync => Assert.Contains(sync.Direction, new[] { "resume", "checkpoint" }));
    }

    [Fact]
    public async Task Clone_keeps_an_existing_local_clone_and_its_uncommitted_edits()
    {
        await using var fixture = await Fixture.CreateAsync();
        var git = fixture.Client();
        var local = await git.CloneAsync(fixture.Workspace, 7);
        await File.WriteAllTextAsync(Path.Combine(local.Path, "draft.txt"), "unsaved");

        var again = await git.CloneAsync(fixture.Workspace, 7);
        await git.FetchAsync(again, 7);

        Assert.Equal("unsaved", await File.ReadAllTextAsync(Path.Combine(again.Path, "draft.txt")));
        await Assert.ThrowsAsync<InvalidOperationException>(() => git.CloneAsync(fixture.Workspace, 8));
    }

    [Fact]
    public async Task A_failed_git_exchange_reports_gits_own_reason()
    {
        await using var fixture = await Fixture.CreateAsync();
        var git = fixture.Client(Path.Combine(fixture.Root, "missing.git"));

        var failure = await Assert.ThrowsAsync<GitWorkspaceCommandException>(() => git.CloneAsync(fixture.Workspace, 7));

        Assert.NotEqual(0, failure.ExitCode);
        Assert.NotEmpty(failure.Detail);
        Assert.False(Directory.Exists(PlatformGitWorkspaceClient.LocalWorkspacePath(fixture.Workspace.WorkspaceId)));
    }

    [Fact]
    public async Task Git_access_needs_a_platform_named_branch_and_a_transport_with_a_git_route()
    {
        await using var fixture = await Fixture.CreateAsync();

        await Assert.ThrowsAsync<NotSupportedException>(() =>
            fixture.Client().CloneAsync(fixture.Workspace with { BranchName = null }, 7));
        await Assert.ThrowsAsync<NotSupportedException>(() =>
            new PlatformGitWorkspaceClient(new Invoker(null)).CloneAsync(fixture.Workspace, 7));
        await Assert.ThrowsAsync<NotSupportedException>(() =>
            new AgentTestRuntime().CreateContext().Platform.Git.CloneAsync(fixture.Workspace, 7));
    }

    [Theory]
    [InlineData("-upload-pack=x")]
    [InlineData("work/a..b")]
    [InlineData("work/a b")]
    [InlineData("work/")]
    [InlineData("work/a.lock")]
    [InlineData("work/a:b")]
    [InlineData("work/a\nb")]
    public async Task A_branch_name_git_could_misread_is_refused(string branch)
    {
        await using var fixture = await Fixture.CreateAsync();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            fixture.Client().CloneAsync(fixture.Workspace with { BranchName = branch }, 7));
    }

    [Theory]
    [InlineData("", "Adrian", "a@b.invalid")]
    [InlineData("message", "", "a@b.invalid")]
    [InlineData("message", "Adrian <x>", "a@b.invalid")]
    [InlineData("message", "Adrian", "a@b.invalid\nCo-authored-by: x")]
    public async Task Commit_refuses_an_unusable_message_or_author(string message, string name, string email)
    {
        await using var fixture = await Fixture.CreateAsync();
        var git = fixture.Client();
        var local = await git.CloneAsync(fixture.Workspace, 7);

        await Assert.ThrowsAsync<ArgumentException>(() => git.CommitAsync(local, new(message, name, email)));
    }

    [Fact]
    public async Task Commit_and_push_refuse_a_directory_the_sdk_did_not_clone()
    {
        await using var fixture = await Fixture.CreateAsync();
        var git = fixture.Client();
        var elsewhere = fixture.Workspace with { Path = fixture.Root };

        await Assert.ThrowsAsync<ArgumentException>(() => git.CommitAsync(elsewhere, Commit));
        await Assert.ThrowsAsync<ArgumentException>(() => git.PushAsync(elsewhere, 7));
        await Assert.ThrowsAsync<ArgumentException>(() => git.FetchAsync(elsewhere, 7));
    }

    private sealed class Invoker(string? remote) : IPlatformToolInvoker, IAgentGitRemote
    {
        public AgentGitRemoteAccess? OpenGitRemote(Guid workspaceId, long assignmentRevision) =>
            remote is null ? null : new AgentGitRemoteAccess(remote, new Dictionary<string, string>());

        public List<GitWorkspaceSyncRequest> Syncs { get; } = [];
        public List<InspectGitWorkspaceRequest> Inspections { get; } = [];
        public List<PublishGitWorkspaceRequest> Publications { get; } = [];

        // Stands in for the platform's sync capability: memory hints and resume notes on "resume",
        // a receipt for the pushed commit on "checkpoint".
        public Task<JsonElement> InvokeAsync(string capability, JsonElement arguments, CancellationToken cancellationToken = default)
        {
            if (capability == GitWorkspaceCapabilities.Inspect)
            {
                var inspect = arguments.Deserialize<InspectGitWorkspaceRequest>(Web)!;
                Inspections.Add(inspect);
                return Task.FromResult(JsonSerializer.SerializeToElement(
                    new GitWorkspaceInspection(inspect.WorkspaceId, "Modified", true, ["b.txt"], []), Web));
            }
            if (capability == GitWorkspaceCapabilities.Publish)
            {
                var publish = arguments.Deserialize<PublishGitWorkspaceRequest>(Web)!;
                Publications.Add(publish);
                return Task.FromResult(JsonSerializer.SerializeToElement(new GitWorkspacePublication(
                    Guid.NewGuid(), publish.WorkspaceId, Guid.NewGuid(), "InternalGit", GitDeliveryKinds.PullRequest, Branch,
                    publish.PushedCommitSha!, new Uri("https://csweet.example/review/1"), "Published"), Web));
            }
            Assert.Equal(PlatformGitWorkspaceClient.SyncCapability, capability);
            var request = arguments.Deserialize<GitWorkspaceSyncRequest>(Web)!;
            Syncs.Add(request);
            Assert.Null(request.Archive);
            var result = request.Direction == "resume"
                ? new GitWorkspaceSyncResult { ResourceProfiles = [new("build", new string('f', 64), 4096)], ResumeSummary = new(new string('e', 64), []) }
                : new GitWorkspaceSyncResult { Checkpoint = new(request.WorkspaceId, request.AssignmentRevision, request.PushedCommitSha!) };
            return Task.FromResult(JsonSerializer.SerializeToElement(result, Web));
        }

        private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

        public async IAsyncEnumerable<JsonElement> InvokeStreamingAsync(string capability, JsonElement arguments,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
        }

        public Task<IReadOnlyList<AgentToolDescriptor>> ListToolsAsync(CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Git access must not list tools.");
    }

    private sealed class Fixture(string root, string remote, GitWorkspaceResult workspace) : IAsyncDisposable
    {
        public string Root { get; } = root;
        public string Remote { get; } = remote;
        public GitWorkspaceResult Workspace { get; } = workspace;

        public Invoker Platform { get; } = new(remote);
        public PlatformGitWorkspaceClient Client(string? otherRemote = null) => new(otherRemote is null ? Platform : new Invoker(otherRemote));

        public static async Task<Fixture> CreateAsync()
        {
            var root = Path.Combine(Path.GetTempPath(), "csweet-git-tests-" + Guid.NewGuid().ToString("N"));
            var remote = Path.Combine(root, "remote.git");
            var seed = Path.Combine(root, "seed");
            Directory.CreateDirectory(remote);
            Directory.CreateDirectory(seed);
            await RunAsync(remote, "init", "--quiet", "--bare", "--initial-branch=main");
            await RunAsync(seed, "init", "--quiet", "--initial-branch=main");
            await File.WriteAllTextAsync(Path.Combine(seed, "a.txt"), "a\n");
            await RunAsync(seed, "add", "--all");
            await RunAsync(seed, "-c", "user.name=Seed", "-c", "user.email=seed@example.invalid", "commit", "--quiet", "-m", "base");
            await RunAsync(seed, "push", "--quiet", remote, "main");
            var baseCommit = await RunAsync(seed, "rev-parse", "HEAD");
            var workspace = new GitWorkspaceResult(Guid.NewGuid(), Guid.NewGuid(), "/workspace", Guid.NewGuid(),
                "Internal", GitDeliveryKinds.BranchOnly, baseCommit, "Ready", false) { BranchName = Branch };
            return new Fixture(root, remote, workspace);
        }

        public Task<string> GitAsync(string directory, params string[] arguments) => RunAsync(directory, arguments);

        private static async Task<string> RunAsync(string directory, params string[] arguments)
        {
            var result = await GitProcess.RunAsync(directory, arguments, null, CancellationToken.None);
            Assert.True(result.ExitCode == 0, $"git {string.Join(' ', arguments)} failed: {result.Error}");
            return result.Output.Trim();
        }

        public static void Delete(string path)
        {
            if (!Directory.Exists(path)) return;
            foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(path, recursive: true);
        }

        public ValueTask DisposeAsync()
        {
            Delete(PlatformGitWorkspaceClient.LocalWorkspacePath(Workspace.WorkspaceId));
            Delete(Root);
            return ValueTask.CompletedTask;
        }
    }
}
