using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;

namespace CSweet.Agent.SDK.Tests;

/// <summary>
/// Runs the real git client through the real loopback forwarder. The platform is replaced by a
/// transport that answers each smart-HTTP exchange with git's own stateless-rpc programs against a
/// local bare repository, which is how the platform's Git host answers them.
/// </summary>
public sealed class GitForwarderEndToEndTests
{
    private const string Branch = "csweet/task/0123456789abcdef0123456789abcdef/r1";
    private static readonly GitWorkspaceCommitRequest Commit = new("feat(task): add files", "Adrian", "adrian@agents.csweet.invalid");

    [Fact]
    public async Task Git_clones_commits_pushes_and_resumes_through_the_forwarder()
    {
        await using var fixture = await Fixture.CreateAsync();
        var git = fixture.Client();

        var local = await git.MaterializeAsync(fixture.Workspace, 7);
        Assert.Equal("a", (await File.ReadAllTextAsync(Path.Combine(local.Path, "a.txt"))).Trim());

        await File.WriteAllTextAsync(Path.Combine(local.Path, "b.txt"), "b");
        // Incompressible, so the push body is large enough for git to compress and chunk it.
        await File.WriteAllBytesAsync(Path.Combine(local.Path, "asset.bin"), RandomNumberGenerator.GetBytes(3 * 1024 * 1024));
        var commit = await git.CommitAsync(local, Commit);
        var pushed = await git.PushAsync(local, 7);

        Assert.Equal(commit, pushed);
        Assert.Equal(pushed, await fixture.GitAsync(fixture.Remote, "rev-parse", $"refs/heads/{Branch}"));
        Assert.Contains(fixture.Transport.Exchanges, e => e is ("POST", "git-receive-pack"));
        Assert.Contains(fixture.Transport.Exchanges, e => e is ("POST", "git-upload-pack"));

        // A second save with nothing new, then a replacement runtime with an empty disk.
        await git.UploadAsync(local, 7);
        Fixture.Delete(local.Path);
        var resumed = await git.MaterializeAsync(fixture.Workspace, 7);
        Assert.Equal("b", await File.ReadAllTextAsync(Path.Combine(resumed.Path, "b.txt")));
        Assert.Equal(3 * 1024 * 1024, new FileInfo(Path.Combine(resumed.Path, "asset.bin")).Length);
        Assert.Equal(pushed, await fixture.GitAsync(resumed.Path, "rev-parse", "HEAD"));
    }

    [Fact]
    public async Task Git_without_the_forwarder_secret_cannot_use_the_remote()
    {
        await using var fixture = await Fixture.CreateAsync();

        var result = await GitProcess.RunAsync(fixture.Root, ["ls-remote", fixture.Access.RemoteUrl],
            new Dictionary<string, string> { ["NO_PROXY"] = "127.0.0.1", ["no_proxy"] = "127.0.0.1" }, CancellationToken.None);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("403", result.Error);
        Assert.Empty(fixture.Transport.Exchanges);
    }

    /// <summary>Answers Git exchanges the way a smart-HTTP server does, using git's stateless-rpc mode.</summary>
    private sealed class StatelessRpcTransport(string repository) : IAgentGitTransport
    {
        public List<(string Method, string Service)> Exchanges { get; } = [];

        public async Task<AgentGitExchangeResult> ExchangeGitAsync(AgentGitExchange exchange, CancellationToken cancellationToken)
        {
            Assert.True(AgentGitRoutes.IsSupported(exchange.Method, exchange.PathAndQuery));
            var advertise = exchange.Method == "GET";
            var service = advertise ? exchange.PathAndQuery[(exchange.PathAndQuery.IndexOf('=') + 1)..] : exchange.PathAndQuery[(exchange.PathAndQuery.LastIndexOf('/') + 1)..];
            lock (Exchanges) Exchanges.Add((exchange.Method, service));
            var input = exchange.Body.ToArray();
            if (exchange.Headers.TryGetValue("Content-Encoding", out var encoding) && encoding == "gzip")
            {
                using var gzip = new GZipStream(new MemoryStream(input), CompressionMode.Decompress);
                using var plain = new MemoryStream();
                await gzip.CopyToAsync(plain, cancellationToken);
                input = plain.ToArray();
            }
            var arguments = advertise
                ? new[] { service["git-".Length..], "--stateless-rpc", "--advertise-refs", repository }
                : new[] { service["git-".Length..], "--stateless-rpc", repository };
            var output = await RunAsync(arguments, input, cancellationToken);
            if (!advertise) return new AgentGitExchangeResult(200, $"application/x-{service}-result", output);
            var header = $"# service={service}\n";
            var prefix = Encoding.ASCII.GetBytes($"{header.Length + 4:x4}{header}0000");
            return new AgentGitExchangeResult(200, $"application/x-{service}-advertisement", [.. prefix, .. output]);
        }

        private static async Task<byte[]> RunAsync(string[] arguments, byte[] input, CancellationToken cancellationToken)
        {
            var start = new ProcessStartInfo("git")
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true
            };
            foreach (var argument in arguments) start.ArgumentList.Add(argument);
            using var process = Process.Start(start)!;
            var error = process.StandardError.ReadToEndAsync(cancellationToken);
            using var output = new MemoryStream();
            var read = process.StandardOutput.BaseStream.CopyToAsync(output, cancellationToken);
            await process.StandardInput.BaseStream.WriteAsync(input, cancellationToken);
            process.StandardInput.Close();
            await read;
            await process.WaitForExitAsync(cancellationToken);
            Assert.True(process.ExitCode == 0, $"git {string.Join(' ', arguments)} failed: {await error}");
            return output.ToArray();
        }
    }

    private sealed class Invoker(AgentGitRemoteAccess access) : IPlatformToolInvoker, IAgentGitRemote
    {
        private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

        public AgentGitRemoteAccess? OpenGitRemote(Guid workspaceId, long assignmentRevision) => access;

        public Task<JsonElement> InvokeAsync(string capability, JsonElement arguments, CancellationToken cancellationToken = default)
        {
            Assert.Equal(PlatformGitWorkspaceClient.SyncCapability, capability);
            var request = arguments.Deserialize<GitWorkspaceSyncRequest>(Web)!;
            Assert.Null(request.Archive);
            var result = request.Direction == "resume"
                ? new GitWorkspaceSyncResult()
                : new GitWorkspaceSyncResult { Checkpoint = new(request.WorkspaceId, request.AssignmentRevision, request.PushedCommitSha!) };
            return Task.FromResult(JsonSerializer.SerializeToElement(result, Web));
        }

        public async IAsyncEnumerable<JsonElement> InvokeStreamingAsync(string capability, JsonElement arguments,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
        }

        public Task<IReadOnlyList<AgentToolDescriptor>> ListToolsAsync(CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Git access must not list tools.");
    }

    private sealed class Fixture(string root, string remote, GitWorkspaceResult workspace, StatelessRpcTransport transport,
        AgentGitForwarder forwarder) : IAsyncDisposable
    {
        public string Root { get; } = root;
        public string Remote { get; } = remote;
        public GitWorkspaceResult Workspace { get; } = workspace;
        public StatelessRpcTransport Transport { get; } = transport;
        public AgentGitRemoteAccess Access { get; } = new(forwarder.RemoteUrl(workspace.WorkspaceId, 7), forwarder.GitEnvironment());

        public PlatformGitWorkspaceClient Client() => new(new Invoker(Access));

        public static async Task<Fixture> CreateAsync()
        {
            var root = Path.Combine(Path.GetTempPath(), "csweet-git-e2e-" + Guid.NewGuid().ToString("N"));
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
            var workspace = new GitWorkspaceResult(Guid.NewGuid(), Guid.NewGuid(), "/workspace", Guid.NewGuid(),
                "InternalGit", GitDeliveryKinds.PullRequest, await RunAsync(seed, "rev-parse", "HEAD"), "Ready", false) { BranchName = Branch };
            var transport = new StatelessRpcTransport(remote);
            var forwarder = new AgentGitForwarder(transport, NullLogger.Instance);
            forwarder.Start();
            return new Fixture(root, remote, workspace, transport, forwarder);
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

        public async ValueTask DisposeAsync()
        {
            await forwarder.DisposeAsync();
            Delete(PlatformGitWorkspaceClient.LocalWorkspacePath(Workspace.WorkspaceId));
            Delete(Root);
        }
    }
}
