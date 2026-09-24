using System.Text.Json;

namespace CSweet.Agent.SDK.Tests;

public sealed class ProjectAssignmentTests
{
    [Fact]
    public async Task ReadProjectAssignment_ReturnsServerResolvedSnapshot()
    {
        var workstreamId = Guid.NewGuid();
        var teamId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var assignedAt = DateTimeOffset.UtcNow;
        var runtime = new AgentTestRuntime()
            .RegisterCapability<object, ProjectAssignmentResponse>(
                PlatformCapabilities.ProjectAssignmentRead,
                (_, _) => Task.FromResult(new ProjectAssignmentResponse(
                    new AssignedProjectContext(workstreamId, "Project", teamId, boardId, "Developer", 4, assignedAt))));

        var assignment = await runtime.CreateContext().Platform.ReadProjectAssignmentAsync();

        Assert.NotNull(assignment);
        Assert.Equal(workstreamId, assignment!.WorkstreamId);
        Assert.Equal("Project", assignment.ProjectName);
        Assert.Equal(teamId, assignment.TeamId);
        Assert.Equal(boardId, assignment.BoardId);
        Assert.Equal(4, assignment.Revision);
    }

    [Fact]
    public async Task ReadProjectAssignment_ReturnsNullWhenUnassigned()
    {
        var runtime = new AgentTestRuntime()
            .RegisterCapability<object, ProjectAssignmentResponse>(
                PlatformCapabilities.ProjectAssignmentRead,
                (_, _) => Task.FromResult(new ProjectAssignmentResponse(null)));

        Assert.Null(await runtime.CreateContext().Platform.ReadProjectAssignmentAsync());
    }

    [Fact]
    public void AssignedProject_IsExposedFromRuntimeContext()
    {
        var identity = new AgentIdentity("emp", "Agent", null, null, null, [], null, null, null)
        {
            AssignedProject = new AssignedProjectContext(Guid.NewGuid(), "Project", null, null, null, 1, DateTimeOffset.UtcNow)
        };

        Assert.NotNull(new AgentTestRuntime().CreateContext(identity: identity).AssignedProject);
        Assert.Null(new AgentTestRuntime().CreateContext().AssignedProject);
    }

    [Fact]
    public async Task AssignedEvent_DispatchesToAssignedHook()
    {
        var agent = new RecordingAssignmentAgent();
        var change = new ProjectAssignmentChangedEvent(Guid.NewGuid(), Guid.NewGuid(), null, null, "Assigned", 2, DateTimeOffset.UtcNow);

        await new AgentTestRuntime().DeliverEventAsync(agent, ProjectAssignmentEvents.Assigned, change);

        Assert.Equal(1, agent.AssignedCount);
        Assert.Equal(0, agent.RemovedCount);
    }

    [Fact]
    public async Task RemovedEvent_DispatchesToRemovedHook()
    {
        var agent = new RecordingAssignmentAgent();
        var change = new ProjectAssignmentChangedEvent(Guid.NewGuid(), Guid.NewGuid(), null, null, "Removed", 3, DateTimeOffset.UtcNow);

        await new AgentTestRuntime().DeliverEventAsync(agent, ProjectAssignmentEvents.Removed, change);

        Assert.Equal(0, agent.AssignedCount);
        Assert.Equal(1, agent.RemovedCount);
    }

    [Fact]
    public async Task UnknownEvent_DoesNotTriggerAssignmentHooks()
    {
        var agent = new RecordingAssignmentAgent();

        await new AgentTestRuntime().DeliverEventAsync(agent, "example.event.v1", new { });

        Assert.Equal(0, agent.AssignedCount);
        Assert.Equal(0, agent.RemovedCount);
    }

    private sealed class RecordingAssignmentAgent : CSweetAgentBase
    {
        public override string AgentId => "com.example.assignment";
        public override string Version => "1.0.0";
        public int AssignedCount { get; private set; }
        public int RemovedCount { get; private set; }

        protected override Task OnProjectAssignedAsync(
            ProjectAssignmentChangedEvent change,
            AgentEventEnvelope message,
            AgentRuntimeContext context,
            CancellationToken cancellationToken)
        {
            AssignedCount++;
            return Task.CompletedTask;
        }

        protected override Task OnProjectRemovedAsync(
            ProjectAssignmentChangedEvent change,
            AgentEventEnvelope message,
            AgentRuntimeContext context,
            CancellationToken cancellationToken)
        {
            RemovedCount++;
            return Task.CompletedTask;
        }
    }
}
