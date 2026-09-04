# Creating a C-Sweet agent

This guide takes a .NET developer from an empty directory to an agent that can be reviewed and
imported by C-Sweet. C-Sweet agents are ordinary hosted .NET applications with a small callback
surface. The SDK owns runtime transport, authentication, work leasing, retries, and shutdown.

## Choose the platform architecture first

A high-quality agent is usually small orchestration code over C-Sweet's durable systems. Choose the
primitive that owns each responsibility before implementing handlers:

| Need | Use |
|---|---|
| Perform bounded typed work for another component | A versioned capability in `provides` |
| Retain an agent-owned obligation across turns or restarts | Personal-todo cards and their wake events |
| Track shared, assignable business work | Work boards, items, sprints, and automations |
| Produce a reviewable deliverable | A revisioned artifact and its canonical decision record |
| Ask a person a bounded question | `platform.user-input.request.v1` |
| Collaborate with another agent across turns | A typed coordination session and artifacts |
| Generate or interpret content | The platform chat client with current model-visible tools |
| Retain an operating assessment | Agent operating state |
| Recall supporting narrative context | Granted memory reads/proposals; never workflow authority |
| Stream one interactive answer | One `AgentTurnStreamWriter` with a single final commit |
| Report non-chat execution status | Bounded `ReportProgressAsync` updates |

Do not replace these systems with an in-memory queue, retained chat transcript, polling loop, direct
database access, or provider credentials. A callback may be delivered again and the process may stop
between any two awaits, so the durable platform record—not the process—is the source of truth.

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
  --SdkVersion 3.27.0
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

Manifest declaration is requested authority, not proof that every invocation will succeed. The
reviewed manifest, installation approval, current organization/resource scope, and any required
provider binding are all evaluated at runtime. A capability can therefore be approved for the
installation while a particular target or action is still denied. Do not self-grant, select another
installation, or retry an authorization failure as if it were a temporary outage. Preserve completed
work and report the exact owner action or scope required.

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

Set model behavior explicitly for the operation:

```csharp
var options = new ChatOptions
{
    Temperature = 0.2f,
    MaxOutputTokens = 2_048,
    Reasoning = new ReasoningOptions
    {
        Effort = ReasoningEffort.Low,
        Output = ReasoningOutput.None
    }
};

var response = await chatClient.GetResponseAsync(messages, options, cancellationToken);
```

Those values are examples, not global defaults. Use lower temperature for extraction and typed
decisions, and choose a larger creative budget only when the requested deliverable needs it. Request
reasoning output only when the agent genuinely consumes or displays provider-emitted reasoning.
Provider token accounting varies; reasoning can consume the available output budget before final
text. A detailed multi-section deliverable therefore needs both an adequate limit and a bounded
scope.

After every model call, validate the result before creating messages, artifacts, or work items:

- require non-empty final content;
- validate required sections or deserialize into the intended typed schema;
- reject obvious truncation or incomplete tool-call cycles when that signal is available;
- use a small, explicit validation-retry budget;
- finish all model retries before the first external mutation.

An empty or structurally invalid response is a model outcome, not permission to continue with an
empty artifact. Return a safe actionable failure or preserve the accepted request for a later retry.
Do not ask the user to re-enter direction that the agent has already stored.

For interactive chat, create one durable turn stream and forward model updates as they arrive:

```csharp
await using var stream = context.CreateTurnStream(conversationId, turnId, attempt);
await stream.ActivityStartedAsync("Reviewing the request.", cancellationToken: cancellationToken);
await stream.WriteReasoningAsync(providerReasoningDelta, cancellationToken);
await stream.WriteDraftAsync(answerDelta, cancellationToken);
await stream.CompleteReasoningAsync(cancellationToken);
await stream.CommitAsync(validatedAnswer, cancellationToken);
```

Use `ResetDraftAsync` before a validation retry and `FailAsync` for a safe terminal failure.
Only `CommitAsync` contains the authoritative answer. Forward all human-readable reasoning the
provider emits, but never forward protected or encrypted reasoning blobs.
Pass the optional `sensitivity` value to `CreateTurnStream` when the whole trace requires a level
other than `Internal`; the server still applies chat authorization and sensitivity policy.

### Separate generation from persistence

Treat a model-backed workflow as explicit stages: accept input, generate, validate, persist, then
announce completion. Store accepted preferences or brief changes before generation. For work that
cannot safely finish in the current turn, create a correlated personal-todo card and let that card
own generation and persistence.

Once generation has produced valid output, a later artifact, message, or work-item failure must not
cause the whole turn to regenerate while that output remains available. When cross-callback recovery
must avoid regeneration, checkpoint the validated output in an appropriate platform-owned durable
record; never rely on process memory or local disk. Retry only the failed idempotent mutation. If
persistence is optional, return the useful output and clearly say it was not saved. If required
persistence cannot accept the first durable checkpoint, block with the precise missing authority or
dependency and do not automatically loop through generation again.

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
domain validation. The signed manifest is the configuration schema; editing settings never starts
the agent and does not require describe/update capabilities. Override
`OnConfigurationChangedAsync` to apply live resources or return `RestartRequired`.

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

Classify failures by what the workflow should do, not only by the concrete CLR exception type:

| Outcome | Agent behavior |
|---|---|
| Cancellation | Stop promptly and propagate cancellation |
| Transient provider or transport failure | Retry within a small budget or defer durable work |
| Empty, truncated, or invalid model result | Retry validation/generation only; do not mutate state |
| Authorization or approval required | Do not retry; block or explain the required owner action |
| Validation or conflict | Re-read authoritative state, correct the request, or block actionably |
| Lost mutation response | Reconcile by stable idempotency key before creating anything again |

User-facing errors should say what was preserved, what did not happen, and what can unblock the next
attempt. Keep full diagnostic exceptions in runtime logs rather than the conversation.

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

For interactive and model-backed agents, also test that preference-only turns update state without
starting generation, accepted input survives a model failure, empty model output cannot create an
artifact, and retrying a downstream failure does not repeat a completed model call or external
effect.

## 9. Import and run

Commit the root manifest and source. For GitHub import, use a public repository and select the
reviewed commit. For local development, clone the standalone repository as an immediate child of
C-Sweet's local agent catalog or configure `CSweet:AgentCatalog:LocalDirectoryPath`.

C-Sweet previews the exact source, schemas, grants, events, network rules, and activation request.
Installation and hiring remain explicit owner actions. Runtime credentials are supplied only by
C-Sweet inside the isolated container; do not add local credential or MCP configuration.

See [Manifest reference](manifest-reference.md), [Capabilities and events](capabilities-and-events.md),
[Durable agendas, chat intake, and structured interactions](durable-agendas-and-interactions.md),
and [Testing and release](testing-and-release.md) for the review checklists.
