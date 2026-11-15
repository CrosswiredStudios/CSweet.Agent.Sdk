using System.Text.Json;
using CSweet.WorkManagement.Contracts;

namespace CSweet.Agent.SDK.Tests;

public sealed class PlanningContextTests
{
    private static T Empty<T>() => JsonSerializer.Deserialize<T>("{}")!;

    [Fact]
    public void DecisionRecoveryFollowsExplicitReplacementsAndRejectsCyclesOrOtherProjects()
    {
        var project = Guid.NewGuid();
        var chain = Enumerable.Range(0, 12).Select(_ => Empty<DecisionRecord>() with { Id = Guid.NewGuid(), WorkstreamId = project, Status = "Superseded" }).ToArray();
        for (int i = 0; i < chain.Length - 1; i++) chain[i] = chain[i] with { SupersededByDecisionId = chain[i + 1].Id };
        chain[^1] = chain[^1] with { Status = "Pending" };
        Assert.Equal(chain[^1], DecisionContinuations.Current(chain[0].Id, project, chain));
        chain[^1] = chain[^1] with { SupersededByDecisionId = chain[0].Id };
        Assert.Null(DecisionContinuations.Current(chain[0].Id, project, chain));
        chain[^1] = chain[^1] with { WorkstreamId = Guid.NewGuid(), SupersededByDecisionId = null };
        Assert.Null(DecisionContinuations.Current(chain[0].Id, project, chain));
    }

    [Fact]
    public async Task CurrentRosterApprovalAndReadinessAreSeparateAndOtherProjectsCannotAuthorizeWork()
    {
        var project = Guid.NewGuid(); var team = Guid.NewGuid(); var other = Guid.NewGuid();
        var roster = new AgentTeamContext(team.ToString(), "team", "Team", 4, Guid.NewGuid().ToString(), "Manager",
            [new("engineer", "Existing engineer", "Agent", null, null, "Member", "Available") { DeclaredRoleKeys = ["engineer"], EffectiveCapabilities = ["implement"] }], [], 1, false);
        var pending = Empty<ResourceChangeRequestResponse>() with { Id = Guid.NewGuid(), WorkstreamId = project, TeamId = team,
            Status = "Pending", DeliveryStatus = "Pending", Roles = [] };
        var approved = pending with { Id = Guid.NewGuid(), Status = "Approved", DeliveryStatus = "Fulfilled" };
        var resource = Empty<ResourceRequestState>() with { Id = Guid.NewGuid(), WorkstreamId = project, Revision = 1, Status = "PendingApproval", PlanHash = "exact" };
        var runtime = new AgentTestRuntime()
            .RegisterCapability<TeamRosterV2Request, TeamRosterV2Response>(PlatformCapabilities.TeamRosterReadV2, (r, _) => {
                Assert.Equal(project, r.WorkstreamId); Assert.Equal(team, r.TeamId); return Task.FromResult(new TeamRosterV2Response(roster, project)); })
            .RegisterCapability<ResourceChangeReadRequest, ResourceChangeReadResponse>(PlatformCapabilities.ResourceChangeRead, (_, _) =>
                Task.FromResult(new ResourceChangeReadResponse([pending, approved, approved with { Id = Guid.NewGuid(), WorkstreamId = other }])))
            .RegisterCapability<ResourceRead, IReadOnlyList<ResourceRequestState>>(ResourceCapabilities.Read, (r, _) => {
                Assert.Equal(project, r.WorkstreamId); return Task.FromResult<IReadOnlyList<ResourceRequestState>>([resource, resource with { WorkstreamId = other }]); });
        var platform = runtime.CreateContext().Platform;
        var first = await PlanningResourceContext.ReadAsync(platform, project, team);
        Assert.Single(first.Roster!.Members); Assert.Equal(2, first.Staffing.Count); Assert.Equal("PendingApproval", Assert.Single(first.Resources).Status);
        resource = resource with { Revision = 2, Status = "Approved" };
        var installing = await PlanningResourceContext.ReadAsync(platform, project, team);
        Assert.NotEqual(first.Fingerprint, installing.Fingerprint); Assert.Equal("Approved", installing.Resources[0].Status);
        resource = resource with { Revision = 3, Status = "Ready", ProviderInstallationId = Guid.NewGuid() };
        var ready = await PlanningResourceContext.ReadAsync(platform, project, team);
        Assert.NotEqual(installing.Fingerprint, ready.Fingerprint);
        Assert.Equal(ready.Fingerprint, (ready with { Resources = [resource with { UpdatedAt = DateTimeOffset.UtcNow, Purpose = "Reworded same purpose" }], Staffing = ready.Staffing.Reverse().ToArray() }).Fingerprint);
        resource = resource with { Revision = 4, Status = "CapacityUnavailable" };
        Assert.NotEqual(ready.Fingerprint, (await PlanningResourceContext.ReadAsync(platform, project, team)).Fingerprint);
    }

    [Theory]
    [InlineData("revision")][InlineData("wrong-team")][InlineData("empty-page")]
    public async Task IncompleteOrDriftingRosterCannotBecomePlanningEvidence(string scenario)
    {
        var project = Guid.NewGuid(); var team = Guid.NewGuid();
        var roster = new AgentTeamContext(team.ToString(), "t", "T", 1, "lead", "Lead",
            [new("one", "One", "Agent", null, null, "Member", "Available")], [], 101, true);
        var runtime = new AgentTestRuntime().RegisterCapability<TeamRosterV2Request, TeamRosterV2Response>(PlatformCapabilities.TeamRosterReadV2, (r, _) =>
            Task.FromResult(new TeamRosterV2Response(r.Page == 1 ? roster : roster with { Revision = scenario == "revision" ? 2 : 1,
                TeamId = scenario == "wrong-team" ? Guid.NewGuid().ToString() : team.ToString(), Members = [], HasMore = scenario == "empty-page" }, project)));
        await Assert.ThrowsAsync<InvalidOperationException>(() => PlanningResourceContext.ReadAsync(runtime.CreateContext().Platform, project, team));
    }

    [Theory]
    [InlineData("valid", true)][InlineData("old-finalization", true)][InlineData("wrong-key", false)]
    [InlineData("wrong-speaker", false)][InlineData("partial", false)][InlineData("blocked", false)]
    [InlineData("changed-source", false)][InlineData("changed-manager", false)][InlineData("changed-project", false)]
    public async Task PlanningAdviceReceiptsBindTheParticipantsScopeAndCurrentAcceptedSource(string scenario, bool valid)
    {
        var project = Guid.NewGuid(); var board = Guid.NewGuid(); var doc = Guid.NewGuid(); var rev = Guid.NewGuid();
        var manager = new AgentCoordinationParticipant(Guid.NewGuid(), Guid.NewGuid(), "Manager", "Manager");
        var specialist = new AgentCoordinationParticipant(Guid.NewGuid(), Guid.NewGuid(), "Specialist", "Specialist");
        var invitation = new WorkContributionRequest(project, board, "scope", "Who owns input?", [new(doc, rev, "sha")]);
        var turns = new List<AgentCoordinationTurn> {
            new(Guid.NewGuid(), 0, manager.OrganizationUserId, "Continue", "Question", DateTimeOffset.UtcNow,
                new(WorkContributions.RequestType, "1.0", "request", 1, true, JsonSerializer.SerializeToElement(invitation), "request-sha")),
            new(Guid.NewGuid(), 1, scenario == "wrong-speaker" ? Guid.NewGuid() : specialist.OrganizationUserId,
                scenario == "blocked" ? "Blocked" : "Completed", "Advice", DateTimeOffset.UtcNow,
                new(WorkContributions.AnswerType, "1.0", scenario == "wrong-key" ? "other" : "request", 1, scenario != "partial",
                    JsonSerializer.SerializeToElement(new WorkContributionAnswer("Existing responsibility", "Engineering owns mouse and keyboard verification", "Execute later against the build")), "answer-sha")) };
        if (scenario == "old-finalization") turns.Add(new(Guid.NewGuid(), 2, manager.OrganizationUserId, "Blocked",
            "Pitch refinement requires the exact pitch, accepted GDD, project/team context and Video Game Director manager.", DateTimeOffset.UtcNow));
        var runtime = new AgentTestRuntime()
            .RegisterCapability<WorkBoardReference, WorkBoardDetail>(WorkItemCapabilities.Read, (_, _) => Task.FromResult(new WorkBoardDetail(
                new(board, "Project", "", false, false, 1, []) { WorkstreamId = scenario == "changed-project" ? Guid.NewGuid() : project,
                    ManagerOrganizationUserId = scenario == "changed-manager" ? Guid.NewGuid() : manager.OrganizationUserId }, [], [])))
            .RegisterCapability<object, ArtifactDocument>(PlatformCapabilities.ArtifactRead, (_, _) => Task.FromResult(new ArtifactDocument(doc, "Brief", "scope", "Accepted",
                rev, null, scenario == "changed-source" ? Guid.NewGuid() : rev, null, null, [new(rev, 1, null, "Scope", "sha", "Accepted", DateTimeOffset.UtcNow, null, null)]) { WorkstreamId = project }));
        var request = new AgentCoordinationTurnRequest(Guid.NewGuid(), 2, 2, "Any subject", "Question", [], manager, specialist, true, turns) {
            SourceKind = "Board", BoardSource = new(board), WorkContext = new(Guid.NewGuid(), project, null, board, null, null, null, Guid.NewGuid(), null, null) };
        Assert.True(WorkContributions.IsReply(request));
        Assert.Equal(valid ? "Completed" : "Blocked", (await WorkContributions.ReceiveAsync(request, runtime.CreateContext())).Disposition);
    }
}
