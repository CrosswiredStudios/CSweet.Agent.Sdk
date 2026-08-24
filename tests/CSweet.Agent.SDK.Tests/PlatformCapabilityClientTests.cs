using System.Text.Json;

namespace CSweet.Agent.SDK.Tests;

public sealed class PlatformCapabilityClientTests
{
    [Fact]
    public async Task TypedOperatingStateHelpers_PreserveAssessmentAndConcurrencyFields()
    {
        AgentOperatingStateWriteRequest? captured = null;
        var now = DateTimeOffset.UtcNow;
        var runtime = new AgentTestRuntime()
            .RegisterCapability<AgentOperatingStateWriteRequest, AgentOperatingStateResponse>(
                PlatformCapabilities.AgentOperatingStateWrite,
                (request, _) =>
                {
                    captured = request;
                    return Task.FromResult(new AgentOperatingStateResponse(
                        Guid.NewGuid(), request.StateKey, request.SchemaId, request.SchemaVersion,
                        request.Status, request.SourceRevisions, request.ConditionCodes,
                        request.DecisionFingerprint, request.OpenCommitmentCorrelations,
                        request.AttentionReviewId, request.Payload, 8, now, now));
                });
        var reviewId = Guid.NewGuid();

        var result = await runtime.CreateContext().Platform.WriteOperatingStateAsync(
            new WriteAgentOperatingStateRequest<TestAssessment>(
                "architect", "com.example.architect", 1, "Degraded",
                new Dictionary<string, string> { ["board"] = "12" },
                ["developer-blocked"], "fingerprint", ["architecture-support:item"],
                reviewId, new TestAssessment("NeedsSupport", 3), 7, "assessment-8"));

        Assert.NotNull(captured);
        Assert.Equal(7, captured!.ExpectedRevision);
        Assert.Equal("assessment-8", captured.IdempotencyKey);
        Assert.Equal(reviewId, result.AttentionReviewId);
        Assert.Equal("NeedsSupport", result.Payload.Health);
        Assert.Equal(3, result.Payload.OpenIssues);
        Assert.Equal(8, result.Revision);
    }

    [Fact]
    public async Task CompleteRoster_RestartsOnceWhenRevisionChangesDuringPaging()
    {
        var calls = 0;
        var runtime = new AgentTestRuntime().RegisterCapability<TeamRosterRequest, TeamRosterResponse>(
            PlatformCapabilities.TeamRosterRead,
            (request, _) =>
            {
                calls++;
                var revision = calls <= 2 ? calls : 3;
                var member = new AgentTeammate(
                    $"employee-{calls}", $"Member {calls}", "Agent", "Developer", "Developer",
                    "Peer", "Available");
                return Task.FromResult(new TeamRosterResponse(new AgentTeamContext(
                    "team", "TEAM", "Delivery", revision, "lead", "Lead", [member], [],
                    2, request.Page == 1)));
            });

        var team = await runtime.CreateContext().Platform.ReadCompleteTeamRosterAsync(pageSize: 1);

        Assert.NotNull(team);
        Assert.Equal(3, team!.Revision);
        Assert.Equal(2, team.Members.Count);
        Assert.Equal(4, calls);
    }

    [Fact]
    public async Task ExactModelToolResolver_FailsClosedAndReturnsOnlyRequestedVisibleBindings()
    {
        var runtime = new AgentTestRuntime()
            .RegisterCapability<object, JsonElement>(
                "work.item.read", (_, _) => Task.FromResult(JsonSerializer.SerializeToElement(new { })),
                modelVisible: true)
            .RegisterCapability<object, JsonElement>(
                "work.item.retry", (_, _) => Task.FromResult(JsonSerializer.SerializeToElement(new { })),
                modelVisible: false);
        var platform = runtime.CreateContext().Platform;

        var tools = await platform.GetModelToolsAsync(["work.item.read"]);

        Assert.Single(tools);
        await Assert.ThrowsAsync<PlatformCapabilityException>(() =>
            platform.GetModelToolsAsync(["work.item.retry"]));
        await Assert.ThrowsAsync<PlatformCapabilityException>(() =>
            platform.GetModelToolsAsync(["work.item.missing"]));
        await Assert.ThrowsAsync<PlatformCapabilityException>(() =>
            platform.GetModelToolsAsync(["work.item.read", "work.item.read"]));
    }

    private sealed record TestAssessment(string Health, int OpenIssues);
}
