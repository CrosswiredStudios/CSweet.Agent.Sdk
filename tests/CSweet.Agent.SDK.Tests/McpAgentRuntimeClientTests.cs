using CSweet.Agent.Contracts.Packaging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Net;
using System.Text;
using System.Text.Json;

namespace CSweet.Agent.SDK.Tests;

public sealed class McpAgentRuntimeClientTests
{
    [Fact]
    public async Task InitializeAsync_HungBrokerRequestFailsWithinConfiguredBoundary()
    {
        var tokenPath = Path.GetTempFileName();
        await File.WriteAllTextAsync(tokenPath, "workload-token");
        try
        {
            using var http = new HttpClient(new HangingHandler());
            await using var client = new McpAgentRuntimeClient(
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
                TimeSpan.FromMilliseconds(50),
                TimeSpan.FromMilliseconds(50));

            var exception = await Assert.ThrowsAsync<TimeoutException>(() =>
                client.InitializeAsync(Manifest(), CancellationToken.None));

            Assert.Contains("initialize", exception.Message);
            Assert.Contains("MCP broker did not respond", exception.Message);
        }
        finally
        {
            File.Delete(tokenPath);
        }
    }

    [Fact]
    public async Task InitializeAsync_RateLimitRetriesAndDoesNotClassifyEmptyBodyAsInvalidJson()
    {
        var tokenPath = Path.GetTempFileName();
        await File.WriteAllTextAsync(tokenPath, "workload-token");
        try
        {
            var handler = new RateLimitedHandler(succeedOnAttempt: 3);
            using var http = new HttpClient(handler);
            await using var client = new McpAgentRuntimeClient(
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
                TimeSpan.FromSeconds(2),
                TimeSpan.FromMilliseconds(1));

            var session = await client.InitializeAsync(Manifest(), CancellationToken.None);

            Assert.Equal("session-1", session.SessionId);
            Assert.Equal(3, handler.Attempts);
        }
        finally
        {
            File.Delete(tokenPath);
        }
    }

    [Fact]
    public async Task InitializeAsync_ExhaustedRateLimitIsRetryableTransportFailure()
    {
        var tokenPath = Path.GetTempFileName();
        await File.WriteAllTextAsync(tokenPath, "workload-token");
        try
        {
            using var http = new HttpClient(new RateLimitedHandler(succeedOnAttempt: int.MaxValue));
            await using var client = new McpAgentRuntimeClient(
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
                TimeSpan.FromSeconds(2),
                TimeSpan.FromMilliseconds(1));

            var exception = await Assert.ThrowsAsync<HttpRequestException>(() =>
                client.InitializeAsync(Manifest(), CancellationToken.None));
            var failure = AgentRuntimeWorker<TestAgent>.DescribeFailure(exception, Guid.Empty);

            Assert.Equal(HttpStatusCode.TooManyRequests, exception.StatusCode);
            Assert.Contains("code=runtime.rate_limited;retryable=true", failure, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(tokenPath);
        }
    }

    [Fact]
    public async Task InvokeStreamingAsync_preserves_frames_and_rejects_out_of_order_sequences()
    {
        var ordered = string.Join('\n',
            "event: capability", "data: {\"sequence\":0,\"hasMore\":true,\"structuredContent\":{\"text\":\"first\"}}", "",
            "event: capability", "data: {\"sequence\":0,\"hasMore\":true,\"structuredContent\":{\"text\":\"duplicate\"}}", "",
            "event: capability", "data: {\"sequence\":1,\"hasMore\":false,\"structuredContent\":{\"text\":\"second\"}}", "", "");
        await using (var fixture = await StreamingFixture.CreateAsync(ordered))
        {
            var values = new List<JsonElement>();
            await foreach (var value in fixture.Client.InvokeStreamingAsync("test.stream.v1", JsonSerializer.SerializeToElement(new { })))
                values.Add(value);
            Assert.Equal(["first", "second"], values.Select(value => value.GetProperty("text").GetString()));
        }

        var malformed = string.Join('\n',
            "data: {\"sequence\":1,\"hasMore\":false,\"structuredContent\":{}}", "", "");
        await using (var fixture = await StreamingFixture.CreateAsync(malformed))
        {
            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            {
                await foreach (var _ in fixture.Client.InvokeStreamingAsync("test.stream.v1", JsonSerializer.SerializeToElement(new { }))) { }
            });
        }
    }

    [Fact]
    public async Task InvokeStreamingAsync_falls_back_when_old_host_does_not_support_stream_method()
    {
        await using var fixture = await StreamingFixture.CreateAsync(streamBody: null);
        var values = new List<JsonElement>();

        await foreach (var value in fixture.Client.InvokeStreamingAsync("test.stream.v1", JsonSerializer.SerializeToElement(new { })))
            values.Add(value);

        Assert.Single(values);
        Assert.Equal("fallback", values[0].GetProperty("text").GetString());
        Assert.Equal(1, fixture.Handler.NonStreamingCalls);
    }

    private static AgentManifest Manifest() => new()
    {
        Id = "com.example.timeout-test",
        Name = "Timeout Test",
        Version = "1.0.0",
        Publisher = new AgentPublisher("example", "Example"),
        Runtime = new AgentRuntimeManifest(),
        Protocol = new AgentProtocolManifest("2.0", "2.0")
    };

    private sealed class HangingHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("The cancellation token should end the simulated request.");
        }
    }

    private sealed class RateLimitedHandler(int succeedOnAttempt) : HttpMessageHandler
    {
        public int Attempts { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Attempts++;
            if (Attempts < succeedOnAttempt)
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.TooManyRequests)
                {
                    Content = new ByteArrayContent([])
                });
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
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
            });
        }
    }

    private sealed class TestAgent : CSweetAgentBase
    {
        public override string AgentId => "com.example.failure-test";
        public override string Version => "1.0.0";
    }

    private sealed class StreamingFixture : IAsyncDisposable
    {
        private readonly string _tokenPath;
        private readonly HttpClient _http;

        private StreamingFixture(string tokenPath, HttpClient http, McpAgentRuntimeClient client, ScriptedHandler handler)
        {
            _tokenPath = tokenPath;
            _http = http;
            Client = client;
            Handler = handler;
        }

        public McpAgentRuntimeClient Client { get; }
        public ScriptedHandler Handler { get; }

        public static async Task<StreamingFixture> CreateAsync(string? streamBody)
        {
            var tokenPath = Path.GetTempFileName();
            await File.WriteAllTextAsync(tokenPath, "workload-token");
            var handler = new ScriptedHandler(streamBody);
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
            await client.InitializeAsync(Manifest(), CancellationToken.None);
            return new StreamingFixture(tokenPath, http, client, handler);
        }

        public async ValueTask DisposeAsync()
        {
            await Client.DisposeAsync();
            _http.Dispose();
            File.Delete(_tokenPath);
        }
    }

    private sealed class ScriptedHandler(string? streamBody) : HttpMessageHandler
    {
        public int NonStreamingCalls { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var requestJson = await request.Content!.ReadAsStringAsync(cancellationToken);
            var method = JsonDocument.Parse(requestJson).RootElement.GetProperty("method").GetString();
            if (method == "initialize")
                return JsonResponse(new
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
                });
            if (method == "tools/list")
                return JsonResponse(new
                {
                    jsonrpc = "2.0",
                    id = 1,
                    result = new
                    {
                        tools = new[]
                        {
                            new
                            {
                                name = "test_stream",
                                description = "test",
                                inputSchema = new { type = "object" },
                                _meta = new { csweet = new { capability = "test.stream.v1", modelVisible = true } }
                            }
                        }
                    }
                });
            if (method == "csweet/tools/call-stream")
            {
                if (streamBody is null)
                    return new HttpResponseMessage(HttpStatusCode.NotFound);
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(streamBody, Encoding.UTF8, "text/event-stream")
                };
            }
            if (method == "tools/call")
            {
                NonStreamingCalls++;
                return JsonResponse(new
                {
                    jsonrpc = "2.0",
                    id = 1,
                    result = new { isError = false, structuredContent = new { text = "fallback" } }
                });
            }
            throw new InvalidOperationException($"Unexpected MCP method {method}.");
        }

        private static HttpResponseMessage JsonResponse(object value) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json")
        };
    }
}
