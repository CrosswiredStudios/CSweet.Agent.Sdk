using CSweet.Agent.Contracts.Packaging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

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
}
