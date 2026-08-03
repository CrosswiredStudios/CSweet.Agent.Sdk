namespace CSweet.Agent.SDK.Tests;

public sealed class PlatformCommunicationClientTests
{
    [Fact]
    public async Task StartCoordinationAsync_UsesTypedGrantGovernedCapability()
    {
        var targetId = Guid.NewGuid();
        var targetInstallationId = Guid.NewGuid();
        var initiatorId = Guid.NewGuid();
        var initiatorInstallationId = Guid.NewGuid();
        var sourceConversationId = Guid.NewGuid();
        var sourceTurnId = Guid.NewGuid();
        var sourceMessageId = Guid.NewGuid();
        StartAgentCoordinationRequest? captured = null;
        var now = DateTimeOffset.UtcNow;
        var runtime = new AgentTestRuntime().RegisterCapability<
            StartAgentCoordinationRequest, AgentCoordinationSession>(
            CommunicationCapabilities.CoordinationStart,
            (request, _) =>
            {
                captured = request;
                return Task.FromResult(new AgentCoordinationSession(
                    Guid.NewGuid(), Guid.NewGuid(), sourceConversationId,
                    sourceTurnId, sourceMessageId,
                    new AgentCoordinationParticipant(
                        initiatorId, initiatorInstallationId, "Architect", "Software Architect"),
                    new AgentCoordinationParticipant(
                        targetId, targetInstallationId, "Product Manager", "Product Manager"),
                    request.Subject, request.Objective, request.SuccessCriteria,
                    AgentCoordinationStatuses.Active, 1, 1, targetId, false, null,
                    now, now, []));
            });
        var request = new StartAgentCoordinationRequest(
            targetId, "Delivery planning", "Prepare a decision-ready board",
            ["Acceptance criteria are explicit."], "Please collaborate.",
            sourceConversationId, sourceTurnId, sourceMessageId, "start-1");

        var result = await runtime.CreateContext().Platform.Communication
            .StartCoordinationAsync(request);

        Assert.NotNull(captured);
        Assert.Equal(request.TargetOrganizationUserId, captured!.TargetOrganizationUserId);
        Assert.Equal(request.SourceMessageId, captured.SourceMessageId);
        Assert.Equal(request.IdempotencyKey, captured.IdempotencyKey);
        Assert.Equal(targetId, result.Target.OrganizationUserId);
        Assert.Equal(AgentCoordinationStatuses.Active, result.Status);
    }

    [Fact]
    public async Task BaseAgent_ReturnsSafeUnsupportedCoordinationDisposition()
    {
        var self = new AgentCoordinationParticipant(
            Guid.NewGuid(), Guid.NewGuid(), "Unsupported", "Agent");
        var counterpart = new AgentCoordinationParticipant(
            Guid.NewGuid(), Guid.NewGuid(), "Counterpart", "Agent");
        var request = new AgentCoordinationTurnRequest(
            Guid.NewGuid(), 1, 1, "Subject", "Objective", ["Done"],
            self, counterpart, false, []);

        var result = await new UnsupportedAgent().HandleCoordinationTurnAsync(
            request, new AgentTestRuntime().CreateContext(), CancellationToken.None);

        Assert.Equal(AgentCoordinationDispositions.Blocked, result.Disposition);
        Assert.Contains("does not implement", result.Content, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class UnsupportedAgent : CSweetAgentBase
    {
        public override string AgentId => "com.example.unsupported";
        public override string Version => "1.0.0";
    }
}
