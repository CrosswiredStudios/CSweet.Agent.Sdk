using System.Text.Json;
using CSweet.Agent.SDK.WorkManagement;
using CSweet.WorkManagement.Contracts;

namespace CSweet.Agent.SDK.Tests;

public sealed class SdkParityPlanningAgentTests
{
    private const string DesignCapability = "software-architecture.design.v1";
    private const string PublishCapability = "software-architecture.publish-plan.v1";

    [Fact]
    public async Task PackagedSdkAgentCanProvideArchitectureAndUseGovernedPlanningPrimitives()
    {
        var boardId = Guid.NewGuid();
        var sprintId = Guid.NewGuid();
        var configured = false;
        var preflighted = false;
        var started = false;
        var runtime = new AgentTestRuntime()
            .RegisterCapability<ConfigureWorkBoardRequest, WorkBoardSummary>(
                WorkBoardCapabilities.Configure,
                (request, _) =>
                {
                    configured = true;
                    return Task.FromResult(new WorkBoardSummary(
                        request.BoardId, request.Name, request.Description ?? string.Empty, false, false,
                        request.ExpectedRevision + 1, []));
                })
            .RegisterCapability<StartWorkSprintExecutionRequest, WorkSprintPreflightResult>(
                WorkOrchestrationCapabilities.Preflight,
                (request, _) =>
                {
                    preflighted = true;
                    return Task.FromResult(new WorkSprintPreflightResult(
                        true, request.BoardId, request.SprintId, Guid.NewGuid(), []));
                })
            .RegisterCapability<StartWorkSprintExecutionRequest, JsonElement>(
                WorkOrchestrationCapabilities.Start,
                (request, _) =>
                {
                    started = true;
                    return Task.FromResult(JsonSerializer.SerializeToElement(new
                    {
                        request.BoardId,
                        request.SprintId,
                        status = "Active"
                    }));
                });
        var agent = new ParityArchitectureAgent();

        await runtime.DeliverEventAsync(
            agent,
            CommunicationEvents.MessageReceived,
            new CommunicationMessageReceivedEvent(
                Guid.NewGuid(), Guid.NewGuid().ToString("D"), Guid.NewGuid().ToString("D"),
                "Begin governed planning.",
                new Dictionary<string, string>
                {
                    [CommunicationMessageContextKeys.SenderRole] = "Software Product Manager"
                }));
        var design = await runtime.ExecuteCapabilityAsync(
            agent, DesignCapability, new { productGoal = "Creator onboarding" });
        var publication = await runtime.ExecuteCapabilityAsync(
            agent, PublishCapability, new { boardId, provisional = true });

        var context = runtime.CreateContext();
        var renamed = await context.Platform.Work.ConfigureBoardAsync(
            new ConfigureWorkBoardRequest(boardId, 1, "Creator Onboarding", null, "parity-board-name"));
        var activation = new StartWorkSprintExecutionRequest(
            boardId, sprintId, 1, "parity-sprint-start");
        var preflight = await context.Platform.InvokeAsync<
            StartWorkSprintExecutionRequest, WorkSprintPreflightResult>(
            WorkOrchestrationCapabilities.Preflight, activation);
        if (preflight.IsValid)
            _ = await context.Platform.InvokeAsync<StartWorkSprintExecutionRequest, JsonElement>(
                WorkOrchestrationCapabilities.Start, activation);

        Assert.True(agent.ReceivedTypedMessage);
        Assert.True(design.Succeeded);
        Assert.True(publication.Succeeded);
        Assert.Equal("Creator Onboarding", renamed.Name);
        Assert.True(configured);
        Assert.True(preflighted);
        Assert.True(started);
        var references = typeof(ParityArchitectureAgent).Assembly.GetReferencedAssemblies()
            .Select(x => x.Name).ToArray();
        Assert.DoesNotContain(references, x =>
            x is not null && (x.Contains("SoftwareProductManager", StringComparison.OrdinalIgnoreCase) ||
                              x.Contains("SoftwareArchitect", StringComparison.OrdinalIgnoreCase) ||
                              x.Equals("CSweet.Contracts", StringComparison.OrdinalIgnoreCase)));
    }

    private sealed class ParityArchitectureAgent : CSweetAgentBase
    {
        public override string AgentId => "com.example.sdk-parity-architect";
        public override string Version => "1.0.0";
        public bool ReceivedTypedMessage { get; private set; }

        public override Task HandleEventAsync(
            AgentEventEnvelope message,
            AgentRuntimeContext context,
            CancellationToken cancellationToken)
        {
            if (message.EventType == CommunicationEvents.MessageReceived)
            {
                var received = JsonSerializer.Deserialize<CommunicationMessageReceivedEvent>(
                    message.Data.GetRawText(), new JsonSerializerOptions(JsonSerializerDefaults.Web));
                ReceivedTypedMessage = received is not null && !string.IsNullOrWhiteSpace(received.Message);
            }
            return Task.CompletedTask;
        }

        protected override Task<AgentWorkResult> ExecuteCapabilityCoreAsync(
            AgentCapabilityRequest request,
            AgentRuntimeContext context,
            CancellationToken cancellationToken) =>
            Task.FromResult(request.Capability switch
            {
                DesignCapability => AgentWorkResult.Success(JsonSerializer.SerializeToElement(new
                {
                    dependencyOrder = new[] { "foundation", "vertical-slice" },
                    blockingQuestions = Array.Empty<string>()
                })),
                PublishCapability => AgentWorkResult.Success(JsonSerializer.SerializeToElement(new
                {
                    state = "Provisional",
                    dates = (string?)null,
                    estimates = (decimal?)null,
                    assignments = Array.Empty<Guid>()
                })),
                _ => AgentWorkResult.Failure("Unsupported capability.")
            });
    }
}
