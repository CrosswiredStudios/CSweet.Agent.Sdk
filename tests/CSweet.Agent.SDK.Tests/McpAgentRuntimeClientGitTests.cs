using CSweet.Agent.Contracts.Packaging;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CSweet.Agent.SDK.Tests;

public sealed class McpAgentRuntimeClientGitTests
{
    private const string Workspace = "/git/workspaces/0f8fad5b-d9cb-469f-a165-70867728950e/7";

    [Fact]
    public async Task ExchangeGitAsync_sends_the_session_credentials_and_only_the_git_headers()
    {
        await using var fixture = await Fixture.CreateAsync(initialize: true);
        fixture.Handler.GitResponse = () => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(Encoding.ASCII.GetBytes("0000"))
            {
                Headers = { { "Content-Type", "application/x-git-upload-pack-result" } }
            }
        };

        var result = await fixture.Client.ExchangeGitAsync(
            new AgentGitExchange(
                "POST",
                $"{Workspace}/git-upload-pack",
                new Dictionary<string, string>
                {
                    ["Content-Type"] = "application/x-git-upload-pack-request",
                    ["Git-Protocol"] = "version=2",
                    ["Authorization"] = "Bearer from-the-workload",
                    ["Mcp-Session-Id"] = "forged",
                    ["Cookie"] = "a=b"
                },
                Encoding.ASCII.GetBytes("want")),
            CancellationToken.None);

        Assert.Equal(200, result.StatusCode);
        Assert.Equal("application/x-git-upload-pack-result", result.ContentType);
        Assert.Equal("0000", Encoding.ASCII.GetString(result.Body));
        var sent = Assert.Single(fixture.Handler.GitRequests);
        Assert.Equal("POST", sent.Method);
        Assert.Equal($"http://agenthost{Workspace}/git-upload-pack", sent.Uri);
        Assert.Equal("want", sent.Body);
        Assert.Equal("Bearer session-token", sent.Headers["Authorization"]);
        Assert.Equal("session-1", sent.Headers["Mcp-Session-Id"]);
        Assert.Equal("application/x-git-upload-pack-request", sent.Headers["Content-Type"]);
        Assert.Equal("version=2", sent.Headers["Git-Protocol"]);
        Assert.DoesNotContain("Cookie", sent.Headers.Keys, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExchangeGitAsync_keeps_the_query_of_a_ref_advertisement()
    {
        await using var fixture = await Fixture.CreateAsync(initialize: true);

        await fixture.Client.ExchangeGitAsync(
            new AgentGitExchange("GET", $"{Workspace}/info/refs?service=git-receive-pack",
                new Dictionary<string, string>(), ReadOnlyMemory<byte>.Empty),
            CancellationToken.None);

        var sent = Assert.Single(fixture.Handler.GitRequests);
        Assert.Equal("GET", sent.Method);
        Assert.Equal($"http://agenthost{Workspace}/info/refs?service=git-receive-pack", sent.Uri);
        Assert.Equal(string.Empty, sent.Body);
    }

    [Theory]
    [InlineData("GET", "/mcp")]
    [InlineData("POST", "/mcp")]
    [InlineData("GET", Workspace + "/info/refs?service=git-upload-archive")]
    [InlineData("POST", Workspace + "/../7/git-receive-pack")]
    [InlineData("DELETE", Workspace + "/git-receive-pack")]
    public async Task ExchangeGitAsync_refuses_other_routes_before_sending(string method, string target)
    {
        await using var fixture = await Fixture.CreateAsync(initialize: true);

        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Client.ExchangeGitAsync(
            new AgentGitExchange(method, target, new Dictionary<string, string>(), ReadOnlyMemory<byte>.Empty),
            CancellationToken.None));

        Assert.Empty(fixture.Handler.GitRequests);
    }

    [Fact]
    public async Task ExchangeGitAsync_requires_an_initialized_session()
    {
        await using var fixture = await Fixture.CreateAsync(initialize: false);

        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Client.ExchangeGitAsync(
            new AgentGitExchange("GET", $"{Workspace}/info/refs?service=git-upload-pack",
                new Dictionary<string, string>(), ReadOnlyMemory<byte>.Empty),
            CancellationToken.None));

        Assert.Empty(fixture.Handler.GitRequests);
    }

    private sealed record SentGitRequest(string Method, string Uri, IReadOnlyDictionary<string, string> Headers, string Body);

    private sealed class Handler : HttpMessageHandler
    {
        public List<SentGitRequest> GitRequests { get; } = [];
        public Func<HttpResponseMessage>? GitResponse { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath == "/mcp")
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(new
                    {
                        jsonrpc = "2.0",
                        id = 1,
                        result = new
                        {
                            _meta = new
                            {
                                csweet = new
                                {
                                    accessToken = "session-token",
                                    expiresAt = DateTimeOffset.UtcNow.AddMinutes(10),
                                    sessionId = "session-1",
                                    grantRevision = 1
                                }
                            }
                        }
                    }), Encoding.UTF8, "application/json")
                };
            }
            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var header in request.Headers) headers[header.Key] = string.Join(",", header.Value);
            if (request.Content is not null)
                foreach (var header in request.Content.Headers) headers[header.Key] = string.Join(",", header.Value);
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            GitRequests.Add(new SentGitRequest(request.Method.Method, request.RequestUri.AbsoluteUri, headers, body));
            return GitResponse?.Invoke() ?? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([]) };
        }
    }

    private sealed class Fixture(string tokenPath, HttpClient http, McpAgentRuntimeClient client, Handler handler) : IAsyncDisposable
    {
        public McpAgentRuntimeClient Client { get; } = client;
        public Handler Handler { get; } = handler;

        public static async Task<Fixture> CreateAsync(bool initialize)
        {
            var tokenPath = Path.GetTempFileName();
            await File.WriteAllTextAsync(tokenPath, "workload-token");
            var handler = new Handler();
            var http = new HttpClient(handler);
            var client = new McpAgentRuntimeClient(
                http,
                Options.Create(new AgentRuntimeOptions
                {
                    McpEndpoint = "http://agenthost/mcp",
                    WorkloadTokenFile = tokenPath,
                    InstallationId = Guid.NewGuid().ToString(),
                    BusinessId = Guid.NewGuid().ToString(),
                    RuntimeInstanceId = Guid.NewGuid().ToString(),
                    TickId = Guid.NewGuid().ToString()
                }),
                NullLogger<McpAgentRuntimeClient>.Instance,
                TimeSpan.FromSeconds(2),
                TimeSpan.FromSeconds(2));
            if (initialize)
                await client.InitializeAsync(new AgentManifest
                {
                    Id = "com.example.git-test",
                    Name = "Git Test",
                    Version = "1.0.0",
                    Publisher = new AgentPublisher("example", "Example"),
                    Runtime = new AgentRuntimeManifest(),
                    Protocol = new AgentProtocolManifest("2.0", "2.0")
                }, CancellationToken.None);
            return new Fixture(tokenPath, http, client, handler);
        }

        public async ValueTask DisposeAsync()
        {
            await Client.DisposeAsync();
            http.Dispose();
            File.Delete(tokenPath);
        }
    }
}
