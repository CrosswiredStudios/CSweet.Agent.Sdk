using System.Text.Json;

namespace CSweet.Agent.SDK.Tests;

public sealed class PlatformCommunicationClientTests
{
    [Fact]
    public void CoordinationArtifact_CarriesBoundedPagingMetadataAndPlatformDigest()
    {
        var payload = JsonSerializer.SerializeToElement(new { storyKey = "STORY-01", tasks = new[] { "TASK-01" } });
        var submission = new AgentCoordinationArtifactSubmission(
            "software-architecture.task-proposal.v1", "1.0", "plan:story-01:tasks", 2, true, payload);
        var artifact = new AgentCoordinationArtifact(
            submission.Type, submission.SchemaVersion, submission.Key, submission.PageOrdinal,
            submission.IsFinalPage, submission.Payload, "ABC123");

        Assert.Equal(2, artifact.PageOrdinal);
        Assert.True(artifact.IsFinalPage);
        Assert.Equal("ABC123", artifact.Digest);
        Assert.Equal("STORY-01", artifact.Payload.GetProperty("storyKey").GetString());
    }
    [Fact]
    public void MessageReceivedEvent_ExposesStableWireContractAndAuthoritativeContextKeys()
    {
        var payload = new CommunicationMessageReceivedEvent(
            Guid.NewGuid(), Guid.NewGuid().ToString("D"), Guid.NewGuid().ToString("D"),
            "I am onboarded and ready to begin planning.",
            new Dictionary<string, string>
            {
                [CommunicationMessageContextKeys.SenderOrganizationUserId] = Guid.NewGuid().ToString("D"),
                [CommunicationMessageContextKeys.SenderEmployeeType] = "Agent",
                [CommunicationMessageContextKeys.SenderRole] = "Software Architect"
            });

        Assert.Equal("com.csweet.user.message.received.v1", CommunicationEvents.MessageReceived);
        Assert.Equal("Agent", payload.Context![CommunicationMessageContextKeys.SenderEmployeeType]);
        Assert.Equal("Software Architect", payload.Context[CommunicationMessageContextKeys.SenderRole]);
    }

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
