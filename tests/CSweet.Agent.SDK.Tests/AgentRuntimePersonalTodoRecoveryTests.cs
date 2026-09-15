using CSweet.Agent.Contracts.Packaging;
using CSweet.WorkManagement.Contracts;

namespace CSweet.Agent.SDK.Tests;

public sealed class AgentRuntimePersonalTodoRecoveryTests
{
    [Fact]
    public void RuntimeFailuresExposeSafeClassificationAndDiagnosticInsteadOfExceptionText()
    {
        var diagnosticId = Guid.Parse("11111111-2222-3333-4444-555555555555");
        var exception = new PlatformCapabilityException(
            "software-architecture.design.v2",
            PlatformCapabilityErrorCode.NotFound,
            "sensitive provider detail");

        var failure = AgentRuntimeWorker<TestAgent>.DescribeFailure(exception, diagnosticId);

        Assert.Equal(
            "agent-failure:v1;code=platform.capability.not_found;retryable=false;capability=software-architecture.design.v2;diagnosticId=11111111-2222-3333-4444-555555555555",
            failure);
        Assert.DoesNotContain("sensitive", failure, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RuntimeTransportFailureIncludesSafeFailureTypeAndHttpStatus()
    {
        var failure = AgentRuntimeWorker<TestAgent>.DescribeFailure(
            new HttpRequestException("sensitive endpoint response", null, System.Net.HttpStatusCode.BadGateway), Guid.Empty);

        Assert.Equal(
            "agent-failure:v1;code=runtime.transport;retryable=true;exceptionType=HttpRequestException;httpStatus=502;diagnosticId=00000000-0000-0000-0000-000000000000",
            failure);
        Assert.DoesNotContain("sensitive", failure, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UnboundedPlatformDeadlineDoesNotOverflowTheRuntimeTimer()
    {
        using var cancellation = new CancellationTokenSource();

        var configured = AgentRuntimeWorker<TestAgent>.ConfigureWorkDeadline(
            cancellation,
            DateTimeOffset.MaxValue - DateTimeOffset.UtcNow);

        Assert.True(configured);
        Assert.False(cancellation.IsCancellationRequested);
        Assert.False(AgentRuntimeWorker<TestAgent>.ConfigureWorkDeadline(
            cancellation,
            TimeSpan.Zero));
    }

    private static AgentManifest Manifest(
        IReadOnlyList<string> events,
        IReadOnlyList<AgentRequiredCapability> requires) => new()
        {
            Id = "com.example.recovery",
            Name = "Recovery agent",
            Version = "1.0.0",
            Publisher = new AgentPublisher("example", "Example"),
            Runtime = new AgentRuntimeManifest(),
            Protocol = new AgentProtocolManifest("2.0", "2.0"),
            Events = new AgentEventManifest(events),
            Requires = requires
        };

    private sealed class TestAgent : CSweetAgentBase
    {
        public override string AgentId => "com.example.recovery";
        public override string Version => "1.0.0";
    }
}
