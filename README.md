# C-Sweet Agent SDK

See [Authoring agents under the operating contract](docs/agent-operating-contract.md) for role-policy profiles, exact model-tool exposure, typed attention checkpoints, and memory authority boundaries.

`CSweet.Agent.SDK` 3.41.0 is the supported .NET 10 authoring API for C-Sweet agents and service
plugins. You implement typed callbacks; the SDK privately manages the outbound runtime,
authentication, live grants, durable work, retries, progress, and shutdown.

## Create an agent

Install the template directly from a checkout of this repository:

```powershell
dotnet new install ./templates/CSweet.Agent.Template
dotnet new csweet-agent --name ResearchAgent `
  --AgentId com.example.research-agent `
  --DisplayName "Research Agent" `
  --PublisherId com.example `
  --PublisherName "Example" `
  --AgentVersion 0.1.0 `
  --PrimaryCapability research.answer.v1 `
  --SdkVersion 3.41.0
cd ResearchAgent
dotnet test
```

To author without the template, add the package directly:

```powershell
dotnet add package CSweet.Agent.SDK --version 3.41.0
```

```csharp
using CSweet.Agent.SDK;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);
builder.AddCSweetAgent<MyAgent>();
await builder.Build().RunAsync();

sealed class MyAgent : CSweetAgentBase
{
    public override string AgentId => "com.example.my-agent";
    public override string Version => "0.1.0";
}
```

## Start here

- Humans: [Creating an agent](docs/creating-an-agent.md)
- Codex and other coding agents: [Agent authoring contract](AGENT_AUTHORING.md)
- Manifest fields: [Manifest reference](docs/manifest-reference.md) and
  [JSON Schema](schemas/csweet-plugin.v2.schema.json)
- Grants and events: [Capabilities and events](docs/capabilities-and-events.md) and
  [generated capability reference](GRANTS.md)
- Continuous work and chat: [Durable agendas, chat intake, and structured interactions](docs/durable-agendas-and-interactions.md)
- Import and release: [Testing and release](docs/testing-and-release.md)
- Existing protocol-v1 agents: [Migrating to 1.0](docs/migrating-to-1.0.md)
- Existing SDK 1.x agents: [Migrating to SDK 2.0](docs/migrating-to-2.0.md)

`AgentRuntimeContext.Platform` exposes typed, grant-governed services.
`GetModelToolsAsync()` returns the current model-visible grant.
`CreateChatClient()` provides platform-governed model streaming.
`CreateTurnStream()` writes ordered reasoning, activity, draft, reset, final-commit, and failure
events for an interactive chat turn. Forward only provider-emitted human-readable reasoning;
protected or encrypted reasoning must never be passed to the writer.
`AgentTestRuntime` runs callbacks and fake capabilities entirely in memory.
Git workspace responses support exact-commit preparation, published-branch resumption,
tracked-change inspection, and governed merge status metadata (`None`, `Queued`, `Merged`,
or `Blocked`).

An agent never receives provider credentials, database access, a caller-selected target
installation, raw runtime tokens, transport clients, or queue/lease details. Manifests request
authority; installation grants and live provider bindings remain authoritative.

## SDK development

```powershell
dotnet test CSweetAgentSdk.slnx
dotnet run --project samples/HelloAgent -- --self-test
dotnet pack src/CSweet.Agent.SDK/CSweet.Agent.SDK.csproj -c Release
```

Runtime implementation rules are in [Runtime maintainers](docs/runtime-maintainers.md). Report
security issues privately to the maintainers as described in [SECURITY.md](SECURITY.md).

## Reusable collaboration

See [agent collaboration](docs/collaboration.md) for typed documentation requests, read sharing, clarification, review, exact-revision handoffs, and durable dependency waits in SDK 3.41.0.

## Acknowledged inference waits

SDK 3.41.0 uses negotiated, lease-bound inference polling so acknowledged waiting does not consume the agent execution budget. See [LLM queue and deadlines](docs/llm-queue.md) for states, cancellation, ownership, runtime limits, and deployment requirements.

## Business calendars

Use context.Platform.Calendar.ReadAsync, CreateAsync, UpdateAsync, CancelAsync, or
ScheduleAsync with the typed work-management calendar contracts. Requests are bound to the
runtime business and employee; permission declarations require upgrade review.
Use local wall-clock dates without offsets, an explicit time zone, stable creation keys, and
the last observed revision for edits. Contributors edit their own events; managers can edit all.
Scheduling others follows the reporting hierarchy and never expands execution authority.

Subscribe to com.csweet.calendar.reminder-due.v1 and override HandleCalendarReminderAsync
when role-specific reminder behavior is needed. The default callback reports receipt; scheduled
work is delivered separately through the existing personal work queue.
Calendar.WithToolsAsync(options) adds only approved calendar model tools and operating guidance
to an existing harness. Calendar.GetResponseAsync(client, messages, ...) supplies a bounded
function-invocation loop for simple agents. Preserve all existing execution and approval rules.
## Compute (3.41.0)

Use the [typed compute client](docs/compute.md) through context.Platform.Compute for Linux/Windows environment requests, lifecycle operations, guest commands, port publication, results, and durable change events. The application owns host setup and UAC; grants remain enforced by the broker.

