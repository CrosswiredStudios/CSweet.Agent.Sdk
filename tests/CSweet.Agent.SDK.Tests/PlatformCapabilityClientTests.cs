using System.Text.Json;
using CSweet.Agent.Contracts.Grpc;
using Google.Protobuf;

namespace CSweet.Agent.SDK.Tests;

public sealed class PlatformCapabilityClientTests
{
    [Fact]
    public async Task ReadBusinessProfileAsync_UsesStableCapabilityAndDeserializesResponse()
    {
        var expected = new BusinessProfileResponse(
            Guid.NewGuid(), "Example", "SaaS", "Software", null, null, "Validation", [], [], null,
            [], null, [], [], null, "UTC", 4, 0.5m, new Dictionary<string, ProfileFieldProvenance>());
        var broker = new StubBroker(CapabilityResultFor(expected));

        var actual = await new PlatformCapabilityClient(broker).ReadBusinessProfileAsync();

        Assert.Equal(expected.OrganizationId, actual.OrganizationId);
        Assert.Equal(PlatformCapabilities.BusinessProfileRead, broker.LastCapability);
    }

    [Fact]
    public async Task InvokeAsync_ThrowsStructuredFailure()
    {
        var payload = new PlatformCapabilityError(PlatformCapabilityErrorCode.ApprovalRequired, "Owner approval is required.");
        var broker = new StubBroker(new CapabilityResult
        {
            Succeeded = false,
            Error = payload.Message,
            Payload = ByteString.CopyFrom(JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions))
        });

        var exception = await Assert.ThrowsAsync<PlatformCapabilityException>(
            () => new PlatformCapabilityClient(broker).ReadFinanceProfileAsync());

        Assert.Equal(PlatformCapabilityErrorCode.ApprovalRequired, exception.Code);
        Assert.Equal(PlatformCapabilities.FinanceProfileRead, exception.Capability);
    }

    [Fact]
    public void PlatformToolAdapters_ExposeOnlyGrantedCapabilities()
    {
        var broker = new StubBroker(new CapabilityResult());
        var platform = new PlatformCapabilityClient(broker);
        var grants = new HashSet<string>(StringComparer.Ordinal)
        {
            PlatformCapabilities.WorkforceSearch,
            PlatformCapabilities.ChatDecisionCreate
        };

        var tools = PlatformToolAdapters.Create(platform, grants);

        Assert.Equal(2, tools.Count);
        Assert.Contains(tools, tool => tool.Name == "search_workforce");
        Assert.Contains(tools, tool => tool.Name == "create_executive_decision");
        Assert.Empty(PlatformToolAdapters.Create(platform, new HashSet<string>()));
    }

    [Fact]
    public void ManagementContracts_DeserializeLegacyPayloadsWithBriefingDefaults()
    {
        const string legacy = """
            {
              "cycleId":"00000000-0000-0000-0000-000000000001",
              "summary":"All clear.",
              "completedOutcomes":[],
              "inProgress":[],
              "blockers":[],
              "risks":[],
              "resourceNeeds":[],
              "decisionsNeeded":[],
              "assumptions":[],
              "confidence":0.9,
              "reportedAt":"2026-07-17T12:00:00Z"
            }
            """;

        var report = JsonSerializer.Deserialize<ManagementStatusReport>(legacy, JsonOptions);

        Assert.NotNull(report);
        Assert.Null(report.RequestId);
        Assert.Null(report.Markdown);
        Assert.Empty(report.ImmediateActions);
        Assert.Empty(report.ConversationTopics);
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static CapabilityResult CapabilityResultFor<T>(T value) => new()
    {
        Succeeded = true,
        ContentType = "application/json",
        Payload = ByteString.CopyFrom(JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions))
    };

    private sealed class StubBroker(CapabilityResult result) : IAgentBrokerClient
    {
        public string? LastCapability { get; private set; }
        public Task StartAsync(RegisterAgent registration, CancellationToken cancellationToken) => Task.CompletedTask;
        public async IAsyncEnumerable<BrokerToAgentMessage> ReadAllAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken) { await Task.CompletedTask; yield break; }
        public Task PublishEventAsync(PublishEvent message, string? correlationId = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<CapabilityResult> InvokeCapabilityAsync(RequestCapability request, string? correlationId = null, CancellationToken cancellationToken = default)
        {
            LastCapability = request.Capability;
            result.RequestId = request.RequestId;
            return Task.FromResult(result);
        }
        public Task SendCapabilityResultAsync(CapabilityResult result, string? correlationId = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
