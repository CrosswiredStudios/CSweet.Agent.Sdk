using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;

namespace CSweet.Agent.SDK.Tests;

public sealed class AgentGitForwarderTests
{
    private static readonly Guid WorkspaceId = Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e");
    private const string Workspace = "/git/workspaces/0f8fad5b-d9cb-469f-a165-70867728950e/7";

    [Fact]
    public async Task Relays_a_ref_advertisement_with_its_query_and_returns_the_platform_response()
    {
        var transport = new RecordingTransport(new AgentGitExchangeResult(
            200, "application/x-git-upload-pack-advertisement", Encoding.ASCII.GetBytes("001e# service=git-upload-pack\n0000")));
        await using var forwarder = Started(transport);
        using var http = Client(forwarder);

        using var response = await http.GetAsync($"{forwarder.RemoteUrl(WorkspaceId, 7)}/info/refs?service=git-upload-pack");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/x-git-upload-pack-advertisement", response.Content.Headers.ContentType?.ToString());
        Assert.Equal("001e# service=git-upload-pack\n0000", await response.Content.ReadAsStringAsync());
        var exchange = Assert.Single(transport.Exchanges);
        Assert.Equal("GET", exchange.Method);
        Assert.Equal($"{Workspace}/info/refs?service=git-upload-pack", exchange.PathAndQuery);
        Assert.True(exchange.Body.IsEmpty);
    }

    [Fact]
    public async Task Relays_a_push_body_and_only_the_git_headers()
    {
        var transport = new RecordingTransport(new AgentGitExchangeResult(
            200, "application/x-git-receive-pack-result", Encoding.ASCII.GetBytes("0000")));
        await using var forwarder = Started(transport);
        using var http = Client(forwarder);
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{forwarder.RemoteUrl(WorkspaceId, 7)}/git-receive-pack")
        {
            Content = new ByteArrayContent(Encoding.ASCII.GetBytes("pack-bytes"))
        };
        request.Content.Headers.TryAddWithoutValidation("Content-Type", "application/x-git-receive-pack-request");
        request.Headers.TryAddWithoutValidation("Git-Protocol", "version=2");
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer from-the-workload");
        request.Headers.TryAddWithoutValidation("Cookie", "a=b");
        request.Headers.TryAddWithoutValidation("Mcp-Session-Id", "forged");

        using var response = await http.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var exchange = Assert.Single(transport.Exchanges);
        Assert.Equal("POST", exchange.Method);
        Assert.Equal($"{Workspace}/git-receive-pack", exchange.PathAndQuery);
        Assert.Equal("pack-bytes", Encoding.ASCII.GetString(exchange.Body.Span));
        Assert.Equal("application/x-git-receive-pack-request", exchange.Headers["Content-Type"]);
        Assert.Equal("version=2", exchange.Headers["Git-Protocol"]);
        Assert.DoesNotContain("Authorization", exchange.Headers.Keys, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("Cookie", exchange.Headers.Keys, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("Mcp-Session-Id", exchange.Headers.Keys, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain(AgentGitForwarder.SecretHeader, exchange.Headers.Keys, StringComparer.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("0000000000000000000000000000000000000000000000000000000000000000")]
    public async Task Refuses_a_caller_without_the_forwarder_secret(string? presented)
    {
        var transport = new RecordingTransport(new AgentGitExchangeResult(200, null, []));
        await using var forwarder = Started(transport);
        using var http = new HttpClient();
        using var request = new HttpRequestMessage(
            HttpMethod.Get, $"{forwarder.RemoteUrl(WorkspaceId, 7)}/info/refs?service=git-upload-pack");
        if (presented is not null) request.Headers.TryAddWithoutValidation(AgentGitForwarder.SecretHeader, presented);

        using var response = await http.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(transport.Exchanges);
    }

    [Theory]
    [InlineData("GET", "/mcp")]
    [InlineData("GET", Workspace + "/info/refs")]
    [InlineData("GET", Workspace + "/info/refs?service=git-upload-archive")]
    [InlineData("GET", Workspace + "/info/refs?service=git-upload-pack&x=1")]
    [InlineData("GET", Workspace + "/HEAD")]
    [InlineData("GET", "/git/workspaces/0f8fad5b-d9cb-469f-a165-70867728950e/0/info/refs?service=git-upload-pack")]
    [InlineData("POST", Workspace + "/info/refs?service=git-upload-pack")]
    [InlineData("POST", Workspace + "/git-upload-pack?x=1")]
    [InlineData("PUT", Workspace + "/git-receive-pack")]
    [InlineData("DELETE", Workspace + "/git-receive-pack")]
    public async Task Refuses_everything_outside_the_fixed_routes(string method, string target)
    {
        var transport = new RecordingTransport(new AgentGitExchangeResult(200, null, []));
        await using var forwarder = Started(transport);
        using var http = Client(forwarder);
        using var request = new HttpRequestMessage(new HttpMethod(method), $"http://127.0.0.1:{forwarder.Port}{target}");
        if (method is "POST" or "PUT") request.Content = new ByteArrayContent([]);

        using var response = await http.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(transport.Exchanges);
        Assert.False(AgentGitRoutes.IsSupported(method, target));
    }

    [Fact]
    public async Task Reports_a_transport_failure_as_bad_gateway_and_keeps_serving()
    {
        var transport = new RecordingTransport(new AgentGitExchangeResult(200, null, [])) { FailNext = true };
        await using var forwarder = Started(transport);
        using var http = Client(forwarder);
        var url = $"{forwarder.RemoteUrl(WorkspaceId, 7)}/info/refs?service=git-upload-pack";

        using var failed = await http.GetAsync(url);
        using var recovered = await http.GetAsync(url);

        Assert.Equal(HttpStatusCode.BadGateway, failed.StatusCode);
        Assert.Equal(HttpStatusCode.OK, recovered.StatusCode);
    }

    [Fact]
    public async Task Passes_a_platform_refusal_through_to_git()
    {
        var transport = new RecordingTransport(new AgentGitExchangeResult(
            403, "text/plain", Encoding.UTF8.GetBytes("not authorized for this workspace")));
        await using var forwarder = Started(transport);
        using var http = Client(forwarder);

        using var response = await http.GetAsync($"{forwarder.RemoteUrl(WorkspaceId, 7)}/info/refs?service=git-receive-pack");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("not authorized for this workspace", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Gives_git_the_secret_by_environment_and_keeps_it_out_of_the_remote_url()
    {
        await using var forwarder = Started(new RecordingTransport(new AgentGitExchangeResult(200, null, [])));

        var environment = forwarder.GitEnvironment();
        var remote = forwarder.RemoteUrl(WorkspaceId, 7);

        Assert.Equal($"http://127.0.0.1:{forwarder.Port}{Workspace}", remote);
        Assert.Equal("2", environment["GIT_CONFIG_COUNT"]);
        Assert.Equal("http.extraHeader", environment["GIT_CONFIG_KEY_0"]);
        Assert.StartsWith(AgentGitForwarder.SecretHeader + ": ", environment["GIT_CONFIG_VALUE_0"]);
        var secret = environment["GIT_CONFIG_VALUE_0"][(AgentGitForwarder.SecretHeader.Length + 2)..];
        Assert.Equal(64, secret.Length);
        Assert.DoesNotContain(secret, remote);
        Assert.Equal("http.proxy", environment["GIT_CONFIG_KEY_1"]);
        Assert.Equal(string.Empty, environment["GIT_CONFIG_VALUE_1"]);
        Assert.Equal("0", environment["GIT_TERMINAL_PROMPT"]);
    }

    [Fact]
    public void Remote_url_requires_a_started_forwarder_and_a_current_workspace()
    {
        var forwarder = new AgentGitForwarder(new RecordingTransport(new AgentGitExchangeResult(200, null, [])), NullLogger.Instance);

        Assert.Throws<InvalidOperationException>(() => forwarder.RemoteUrl(WorkspaceId, 7));
        Assert.Throws<ArgumentException>(() => AgentGitRoutes.WorkspacePath(Guid.Empty, 7));
        Assert.Throws<ArgumentException>(() => AgentGitRoutes.WorkspacePath(WorkspaceId, 0));
    }

    private static AgentGitForwarder Started(IAgentGitTransport transport)
    {
        var forwarder = new AgentGitForwarder(transport, NullLogger.Instance);
        forwarder.Start();
        return forwarder;
    }

    private static HttpClient Client(AgentGitForwarder forwarder)
    {
        var http = new HttpClient();
        var header = forwarder.GitEnvironment()["GIT_CONFIG_VALUE_0"];
        http.DefaultRequestHeaders.TryAddWithoutValidation(
            AgentGitForwarder.SecretHeader, header[(AgentGitForwarder.SecretHeader.Length + 2)..]);
        return http;
    }

    private sealed class RecordingTransport(AgentGitExchangeResult result) : IAgentGitTransport
    {
        public List<AgentGitExchange> Exchanges { get; } = [];
        public bool FailNext { get; set; }

        public Task<AgentGitExchangeResult> ExchangeGitAsync(AgentGitExchange exchange, CancellationToken cancellationToken)
        {
            if (FailNext)
            {
                FailNext = false;
                throw new HttpRequestException("The broker is unavailable.");
            }
            // The forwarder reuses its read buffer owner; copy so later assertions see this request.
            Exchanges.Add(exchange with { Body = exchange.Body.ToArray() });
            return Task.FromResult(result);
        }
    }
}
