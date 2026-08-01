using CSweet.Agent.SDK.WorkManagement;
using CSweet.WorkManagement.Contracts;

namespace CSweet.Agent.SDK.Tests;

/// <summary>
/// Compile-checked counterparts for the platform, work-management, configuration, event, and
/// model examples in docs/creating-an-agent.md.
/// </summary>
public sealed class HumanDocumentationSnippetTests
{
    [Fact]
    public async Task TypedPlatformAndWorkExamplesCompileAndRunInMemory()
    {
        var boardId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var runtime = new AgentTestRuntime()
            .RegisterCapability<object, BusinessProfileResponse>(
                PlatformCapabilities.BusinessProfileRead,
                (_, _) => Task.FromResult(new BusinessProfileResponse(
                    Guid.NewGuid(),
                    "Example",
                    "Research",
                    null,
                    "Validate a market",
                    null,
                    null,
                    [],
                    [],
                    null,
                    [],
                    null,
                    [],
                    [],
                    null,
                    "UTC",
                    1,
                    0.5m,
                    new Dictionary<string, ProfileFieldProvenance>())))
            .RegisterCapability<CreateWorkItemRequest, WorkItem>(
                WorkItemCapabilities.Create,
                (request, _) => Task.FromResult(new WorkItem(
                    itemId,
                    Guid.NewGuid(),
                    null,
                    null,
                    request.Kind,
                    request.Title,
                    request.Description ?? string.Empty,
                    "Backlog",
                    request.Priority,
                    null,
                    0,
                    1,
                    request.DueDate)));
        var context = runtime.CreateContext();

        var profile = await context.Platform.ReadBusinessProfileAsync();
        var task = await context.Platform.Work.CreateItemAsync(new CreateWorkItemRequest(
            boardId, "Review the launch brief", null, WorkItemKinds.Epic,
            WorkPriorities.Medium, null, null, null, "launch-review:example"));

        Assert.Equal("Example", profile.Name);
        Assert.Equal(itemId, task.Id);
    }

    [Fact]
    public async Task ConfigurationAndEventExamplesCompileAndRunInMemory()
    {
        var agent = new DocumentedAgent();
        var runtime = new AgentTestRuntime();

        var schema = await runtime.ExecuteCapabilityAsync(
            agent,
            AgentConfigurationCapabilities.Describe,
            new { });
        await runtime.DeliverEventAsync(
            agent,
            ManagementEvents.ReviewDue,
            new { review = "weekly" });

        Assert.True(schema.Succeeded);
        Assert.Single(runtime.Progress);
    }

    [Fact]
    public void ModelHelpersCompileWithoutProviderCredentials()
    {
        var context = new AgentTestRuntime().CreateContext();
        var selection = new AgentLlmSelection(Guid.NewGuid(), "test-model");

        Assert.NotNull(context.CreateChatClient(selection));
    }

    private sealed class DocumentedAgent : CSweetAgentBase
    {
        public override string AgentId => "com.example.documented";
        public override string Version => "0.1.0";

        protected override AgentConfigurationBuilder Configure(AgentConfigurationBuilder builder) =>
            builder.Boolean("includeSources", "Include sources", defaultValue: true);

        public override Task HandleEventAsync(
            AgentEventEnvelope message,
            AgentRuntimeContext context,
            CancellationToken cancellationToken) =>
            message.EventType == ManagementEvents.ReviewDue
                ? context.ReportProgressAsync(new { stage = "review-received" }, cancellationToken)
                : Task.CompletedTask;
    }
}
