using System.Text.Json;
using CSweet.WorkManagement.Contracts;

namespace CSweet.Agent.SDK.Tests;

public sealed class CanonicalExecutionDispatchTests
{
    [Theory]
    [InlineData(WorkExecutionScopes.Task)]
    [InlineData(WorkExecutionScopes.Story)]
    [InlineData(WorkExecutionScopes.Epic)]
    [InlineData(WorkExecutionScopes.Release)]
    public async Task OneCapabilityPreservesTheEntireAssignmentForEveryScope(string scope)
    {
        var assignment = Assignment(scope);
        var agent = new RecordingAgent();
        await new AgentTestRuntime().ExecuteCapabilityAsync(agent, WorkManagementCapabilityNames.ExecutionRun, assignment);
        Assert.NotNull(agent.Received);
        Assert.Equal(JsonSerializer.Serialize(assignment), JsonSerializer.Serialize(agent.Received));
        Assert.Equal(scope == WorkExecutionScopes.Task, agent.TaskHandler);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("unknown")]
    public async Task MissingOrUnknownScopeNeverReachesAnAgentHandler(string? scope)
    {
        var agent = new RecordingAgent();
        await new AgentTestRuntime().ExecuteCapabilityAsync(agent, WorkManagementCapabilityNames.ExecutionRun,
            Assignment(WorkExecutionScopes.Task) with { Scope = scope! });
        Assert.Null(agent.Received);
    }

    [Fact]
    public async Task AggregateCannotClaimSprintAuthority()
    {
        var agent = new RecordingAgent();
        await new AgentTestRuntime().ExecuteCapabilityAsync(agent, WorkManagementCapabilityNames.ExecutionRun,
            Assignment(WorkExecutionScopes.Release) with { SprintId = Guid.NewGuid() });
        Assert.Null(agent.Received);
    }

    private static WorkExecutionAssignment Assignment(string scope) => new(
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
        scope == WorkExecutionScopes.Release ? null : Guid.NewGuid(), scope, Guid.NewGuid(), 7,
        scope == WorkExecutionScopes.Task ? Guid.NewGuid() : null,
        scope == WorkExecutionScopes.Task ? Guid.NewGuid() : null, 4, "BOARD-1",
        scope == WorkExecutionScopes.Task ? Guid.NewGuid() : null, "quality", 0, 1,
        DateTimeOffset.UtcNow.AddHours(1), "Verify exact evidence", JsonSerializer.SerializeToElement(new { }),
        JsonSerializer.SerializeToElement(new { }), [], [])
    { OrganizationUserId = Guid.NewGuid(), AgentInstallationId = Guid.NewGuid(), PlanningRevision = 9,
        PermittedOutcomes = ["approved", "changes_requested"] };

    private sealed class RecordingAgent : CSweetAgentBase
    {
        public override string AgentId => "com.example.execution-test";
        public override string Version => "1.0.0";
        public WorkExecutionAssignment? Received { get; private set; }
        public bool TaskHandler { get; private set; }
        protected override Task<AgentWorkResult> ExecuteCapabilityCoreAsync(AgentCapabilityRequest request,
            AgentRuntimeContext context, CancellationToken cancellationToken)
        {
            Assert.Equal(WorkManagementCapabilityNames.ExecutionRun, request.Capability);
            Received = DeserializePayload<WorkExecutionAssignment>(request.Payload);
            TaskHandler = true;
            return Task.FromResult(AgentWorkResult.Success(new { }));
        }
        protected override Task<AgentWorkResult> ExecuteDeliveryScopeAsync(WorkExecutionAssignment assignment,
            AgentRuntimeContext context, CancellationToken cancellationToken)
        {
            Received = assignment;
            return Task.FromResult(AgentWorkResult.Success(new { }));
        }
    }
}
