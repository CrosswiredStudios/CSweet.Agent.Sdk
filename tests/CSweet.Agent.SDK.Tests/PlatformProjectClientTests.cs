using System.Text.Json;
namespace CSweet.Agent.SDK.Tests;

public sealed class PlatformProjectClientTests
{
    [Fact]
    public async Task Intake_preserves_human_source_revision_and_idempotency_without_setup_mutations()
    {
        var source = Guid.NewGuid(); var chat = Guid.NewGuid(); var intakeId = Guid.NewGuid();
        RetainProjectIntakeRequest? retained = null; ChooseProjectIntakeRequest? chosen = null;
        var summary = new ProjectIntakeSummary(intakeId, "Prototype", "Build a game", "AwaitingProjectChoice", "ask",
            Guid.NewGuid(), Guid.NewGuid(), null, null, null, null, null, chat, source, 1, "/projects/new?intake=" + intakeId, null)
            { OriginalRequest = "Build a game with keyboard controls." };
        var runtime = new AgentTestRuntime()
            .RegisterCapability<RetainProjectIntakeRequest, ProjectIntakeSummary>(ProjectIntakeCapabilities.Retain,
                (request, _) => { retained = request; return Task.FromResult(summary); })
            .RegisterCapability<ChooseProjectIntakeRequest, ProjectIntakeSummary>(ProjectIntakeCapabilities.Choose,
                (request, _) => { chosen = request; return Task.FromResult(summary with { Revision = 2, Status = "AwaitingProjectCreation" }); });
        var projects = runtime.CreateContext().Platform.Projects;
        var result = await projects.RetainAsync(new(chat, source, "Prototype", "Build a game", "ask", null, "retain"));
        var selected = await projects.ChooseAsync(new(result.Id, result.Revision, "create", null, source, "choice"));
        Assert.Equal(chat, retained!.ConversationId); Assert.Equal(source, retained.SourceMessageId);
        Assert.Equal("retain", retained.IdempotencyKey); Assert.Equal("ask", retained.TicketOwner);
        Assert.Equal(1, chosen!.ExpectedRevision); Assert.Equal("choice", chosen.IdempotencyKey);
        Assert.Equal(2, selected.Revision); Assert.Equal(summary.OriginalRequest, selected.OriginalRequest);
        Assert.DoesNotContain(ProjectIntakeCapabilities.All, capability => capability.Contains("membership") || capability.Contains("hiring.approve"));
    }

    [Fact]
    public async Task Project_intake_does_not_silently_fall_back_when_the_host_has_not_granted_it()
    {
        var projects = new AgentTestRuntime().CreateContext().Platform.Projects;
        await Assert.ThrowsAsync<PlatformCapabilityException>(() => projects.ReadAsync(Guid.NewGuid()));
    }
}
