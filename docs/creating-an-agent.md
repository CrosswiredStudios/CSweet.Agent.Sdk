# Creating a C-Sweet agent

This guide takes a .NET developer from an empty directory to an agent that can be reviewed and
imported by C-Sweet. C-Sweet agents are ordinary hosted .NET applications with a small callback
surface. The SDK owns runtime transport, authentication, work leasing, retries, and shutdown.

## 1. Scaffold the repository

Install the template from an SDK checkout and supply stable identities:

```powershell
dotnet new install ../CSweetAgentSdk/templates/CSweet.Agent.Template
dotnet new csweet-agent --name ResearchAgent `
  --AgentId com.example.research-agent `
  --DisplayName "Research Agent" `
  --PublisherId com.example `
  --PublisherName "Example" `
  --AgentVersion 0.1.0 `
  --PrimaryCapability research.answer.v1 `
  --SdkVersion 2.4.0
cd ResearchAgent
dotnet test
```

The template produces a complete repository. Its executable project references the NuGet package,
not the SDK source checkout. The root manifest tells C-Sweet what to build, what work the agent
provides, and what authority it requests.

## 2. Implement typed work

Register one `ICSweetAgent` with the host:

```csharp
var builder = Host.CreateApplicationBuilder(args);
builder.AddCSweetAgent<ResearchAgent>();
await builder.Build().RunAsync();
```

Derive from `CSweetAgentBase`, deserialize a typed input, validate it, and return a typed result:

```csharp
internal sealed record ResearchRequest(string Question);
internal sealed record ResearchResponse(string Answer);

internal sealed class ResearchAgent : CSweetAgentBase
{
    public override string AgentId => "com.example.research-agent";
    public override string Version => "0.1.0";

    protected override Task<AgentWorkResult> ExecuteCapabilityCoreAsync(
        AgentCapabilityRequest request,
        AgentRuntimeContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (request.Capability != "research.answer.v1")
            return Task.FromResult(AgentWorkResult.Failure("Unsupported capability."));

        var input = DeserializePayload<ResearchRequest>(request.Arguments);
        if (input is null || string.IsNullOrWhiteSpace(input.Question))
            return Task.FromResult(AgentWorkResult.Failure("question is required."));

        return Task.FromResult(AgentWorkResult.Success(
            new ResearchResponse($"Received: {input.Question}")));
    }
}
```

`AgentCapabilityRequest.WorkId` identifies the durable C-Sweet work item. It is not automatically a
domain idempotency key for every downstream action. Mutation request contracts expose their own
`IdempotencyKey`; derive a stable value from the business operation and reuse it across retries.

## 3. Handle events and activation

Subscribe in the manifest and override `HandleEventAsync`:

```csharp
public override Task HandleEventAsync(
    AgentEventEnvelope message,
    AgentRuntimeContext context,
    CancellationToken cancellationToken)
{
    cancellationToken.ThrowIfCancellationRequested();
    return message.EventType == ManagementEvents.ReviewDue
        ? context.ReportProgressAsync(new { stage = "review-received" }, cancellationToken)
        : Task.CompletedTask;
}
```

Events are durable work and can be delivered again after a failed attempt. Event callbacks must be
idempotent. `IAgentActivationHandler` is optional and is appropriate for initialization tied to an
interactive, scheduled, manual, or always-on activation. `IAgentConnectedService` is only for
services that genuinely need to run while the SDK session is connected.

## 4. Call C-Sweet services

Use typed methods on `context.Platform`:

```csharp
var profile = await context.Platform.ReadBusinessProfileAsync(cancellationToken);
```

The matching capability must appear in `requires`, and an installer must grant it. An ungranted or
unavailable capability throws `PlatformCapabilityException`; handle expected denial,
unavailability, validation, conflict, approval, and budget outcomes without exposing sensitive
details.

Provider capabilities can be called through `Platform.InvokeAsync<TRequest,TResponse>`. C-Sweet
resolves the approved same-organization provider binding; agent code never selects an installation.

Work boards, items, sprints, and automation have a dedicated typed client:

```csharp
var task = await context.Platform.Work.CreateTaskAsync(
    boardId,
    "Review the launch brief",
    idempotencyKey: $"launch-review:{briefId}",
    cancellationToken: cancellationToken);
```

The request still needs the matching `WorkBoardCapabilities`, `WorkItemCapabilities`,
`WorkSprintCapabilities`, or `WorkAutomationCapabilities` grant. Work-management request and
response contracts come from `CSweet.WorkManagement.Contracts`, which is a dependency of the SDK.

## 5. Use models safely

Model access is a platform capability, not a credential:

```csharp
var selection = new AgentLlmSelection(providerId, model);
var chatClient = context.CreateChatClient(selection);
var tools = await context.GetModelToolsAsync(cancellationToken);
```

Request `platform.llm.chat-stream.v1`, obtain model/provider choices from approved configuration,
and pass only the tools returned for the current live grant. Do not cache model tools across grant
revisions and do not create provider SDK clients with API keys.

## 6. Add configuration

Define author-visible fields with `Configure`:

```csharp
protected override AgentConfigurationBuilder Configure(AgentConfigurationBuilder builder) =>
    builder
        .LlmProvider("llmProviderId", "LLM provider", required: true)
        .LlmModel("llmModel", "Model", "llmProviderId", required: true)
        .Boolean("includeSources", "Include sources", defaultValue: true);
```

Read values through `Settings`, and override `ValidateConfigurationUpdate` for cross-field or
domain validation. If the manifest has `configuration` entries, it must also provide
`agent.configuration.describe.v1` and `agent.configuration.update.v1`.

Secret fields are opaque installation settings; never log or return them. Prefer brokered
credential bindings for external services.

## 7. Report progress and handle failure

Progress should be bounded, useful to a person, and safe to persist:

```csharp
await context.ReportProgressAsync(
    new { stage = "researching", message = "Reviewing approved sources." },
    cancellationToken);
```

The SDK assigns monotonic sequence numbers. Always honor the callback cancellation token. Return
`AgentWorkResult.Failure` for expected invalid or unsupported work. Let cancellation propagate.
Avoid returning stack traces, prompts, credentials, or private model/provider data.

## 8. Test without C-Sweet

`AgentTestRuntime` provides in-memory capabilities and progress:

```csharp
var runtime = new AgentTestRuntime()
    .RegisterCapability<object, object>(
        PlatformCapabilities.BusinessProfileRead,
        (_, _) => Task.FromResult<object>(new { name = "Example" }));

var result = await runtime.ExecuteCapabilityAsync(
    new ResearchAgent(),
    "research.answer.v1",
    new { question = "What should we learn?" });
```

Test successful and malformed inputs, unsupported work, cancellation, progress, every requested
platform capability, and denial when a capability is not registered. Keep manifest identity and
version assertions in the test suite.

## 9. Import and run

Commit the root manifest and source. For GitHub import, use a public repository and select the
reviewed commit. For local development, clone the standalone repository as an immediate child of
C-Sweet's local agent catalog or configure `CSweet:AgentCatalog:LocalDirectoryPath`.

C-Sweet previews the exact source, schemas, grants, events, network rules, and activation request.
Installation and hiring remain explicit owner actions. Runtime credentials are supplied only by
C-Sweet inside the isolated container; do not add local credential or MCP configuration.

See [Manifest reference](manifest-reference.md), [Capabilities and events](capabilities-and-events.md),
and [Testing and release](testing-and-release.md) for the review checklists.
