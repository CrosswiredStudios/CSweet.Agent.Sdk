using CSweet.Agent.Contracts.Packaging;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CSweet.Agent.SDK.Tests;

public sealed class InferencePollingTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task PollingPreservesAcknowledgedWaitButHonorsCancellationAndWorkIdentity(bool cancel, bool wrongWork)
    {
        var path = Path.GetTempFileName();
        await File.WriteAllTextAsync(path, "workload-token");
        try
        {
            var workId = Guid.NewGuid();
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(1));
            var handler = new PollHandler(workId, wrongWork);
            using var http = new HttpClient(handler);
            await using var client = new McpAgentRuntimeClient(http,
                Options.Create(new AgentRuntimeOptions { McpEndpoint = "http://host/mcp", WorkloadTokenFile = path,
                    InstallationId = Guid.NewGuid().ToString(), BusinessId = Guid.NewGuid().ToString(),
                    RuntimeInstanceId = Guid.NewGuid().ToString(), TickId = Guid.NewGuid().ToString() }),
                NullLogger<McpAgentRuntimeClient>.Instance, TimeSpan.FromSeconds(3), TimeSpan.FromMilliseconds(50));
            await client.InitializeAsync(new AgentManifest { Id = "test", Name = "Test", Version = "1.0.0",
                Publisher = new AgentPublisher("test", "Test"), Runtime = new AgentRuntimeManifest(),
                Protocol = new AgentProtocolManifest("2.0", "2.0") }, default);
            var reporter = new Reporter(cancel ? deadline : null);
            using var scope = new InferenceExecutionScope(new(workId, 1, AgentWorkKind.Event, "test",
                JsonSerializer.SerializeToElement(new { }), "lease", DateTimeOffset.UtcNow.AddMinutes(3),
                DateTimeOffset.UtcNow.AddSeconds(1), null, null), deadline, reporter);
            scope.Enter();
            async Task<List<JsonElement>> Read()
            {
                var chunks = new List<JsonElement>();
                await foreach (var chunk in client.InvokeStreamingAsync(PlatformCapabilities.LlmChatStream,
                    JsonSerializer.SerializeToElement(new { }), deadline.Token)) chunks.Add(chunk);
                return chunks;
            }
            if (wrongWork) await Assert.ThrowsAsync<InvalidOperationException>(Read);
            else if (cancel) await Assert.ThrowsAnyAsync<OperationCanceledException>(Read);
            else
            {
                Assert.Equal("Done", Assert.Single(await Read()).GetProperty("text").GetString());
                Assert.False(deadline.IsCancellationRequested);
                Assert.Contains("Queued", reporter.States);
                Assert.Contains("Completed", reporter.States);
            }
            Assert.Equal(cancel || wrongWork, handler.Cancelled);
        }
        finally { File.Delete(path); }
    }

    private sealed class Reporter(CancellationTokenSource? cancel) : IAgentProgressReporter
    {
        public List<string> States { get; } = [];
        public Task ReportAsync(object? value, CancellationToken cancellationToken = default)
        {
            var state = JsonSerializer.SerializeToElement(value).GetProperty("Metadata").GetProperty("llmState").GetString()!;
            States.Add(state);
            if (state == "Queued") cancel?.Cancel();
            return Task.CompletedTask;
        }
    }

    private sealed class PollHandler(Guid workId, bool wrongWork) : HttpMessageHandler
    {
        private int reads;
        public bool Cancelled;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            var method = body.RootElement.GetProperty("method").GetString();
            object result;
            if (method == "initialize") result = new { _meta = new { csweet = new { sessionId = "session", accessToken = "access",
                expiresAt = DateTimeOffset.UtcNow.AddHours(1), grantRevision = 1, llmJobs = true } } };
            else if (method == "tools/list") result = new { tools = new[] { new { name = "llm", description = "LLM",
                inputSchema = new { type = "object" }, _meta = new { csweet = new { capability = PlatformCapabilities.LlmChatStream, modelVisible = false } } } } };
            else if (method == "csweet/llm/start") result = new { jobId = Guid.NewGuid() };
            else if (method == "csweet/llm/cancel") { Cancelled = true; result = new { }; }
            else if (method == "csweet/llm/read")
            {
                var complete = ++reads > 1;
                result = new { workId = wrongWork ? Guid.NewGuid() : workId, workDeadline = DateTimeOffset.UtcNow.AddSeconds(10),
                    state = complete ? "Completed" : "Queued", next = complete ? 1 : 0, completed = complete,
                    chunks = complete ? new[] { new { succeeded = true, payload = new { text = "Done" } } } : [] };
            }
            else throw new InvalidOperationException($"Unexpected method {method}");
            return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new { jsonrpc = "2.0", id = 1, result }), Encoding.UTF8, "application/json") };
        }
    }
}
