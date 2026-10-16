# C-Sweet Agent SDK

SDK 4.5.0 adds `HierarchicalProjectDelivery.PrepareIncrementAsync`: select fully detailed Epic increments while retaining future approved scope on the board. Each selected increment still requires task, Story, Epic and release acceptance, independent reviewers and existing preflight. An active plan cannot silently change its selected increment. See [hierarchical delivery](docs/hierarchical-delivery.md).

SDK 4.4.1 adds bounded retry of interrupted work heartbeat requests. It preserves the
same authenticated attempt and existing timeout; it does not revive expired leases
or retry arbitrary capability mutations. See `docs/runtime-maintainers.md`.

SDK 4.5.0 uses one execution capability, `work.execution.run`, and one scope-aware
`WorkExecutionAssignment`. Staffing, task dispatch and aggregate reviews share the
same contract. The SDK preserves the complete assignment when invoking task handlers,
including principal, planning revision, delivery plan and permitted outcomes.
Upgrade the platform and executing agents together; old execution envelopes have no
compatibility adapter. See [hierarchical delivery](docs/hierarchical-delivery.md).

SDK 3.66.0 uses the shared `WorkArtifactDelivery` contract for document authors and QA, accepts existing PascalCase persisted outcomes, and rejects malformed or ambiguous identities.

SDK 3.64.1 adds `DeliveryBuildReadiness` for certified exact-candidate release builds. Hierarchical staffing prefers the Build Release Engineer when available, then permits a Technical Director or Developer explicitly advertising `work.execution.build-readiness.v1` alongside V2 execution. Build-readiness does not grant deployment, release publication, or independent product approval. Publish this SDK before the consuming agent releases.

SDK 3.62.0 adds `CapabilityNames.Management.StatusReport` to the documented capability catalog,
so agents can request the existing platform reporting capability in their reviewed manifests.

SDK 3.59.1 binds callback platform clients to their work lifetime. Retained clients and inherited
background flows cannot start calls after callback completion or use another callback's client;
late tool results are withheld, and closed callbacks cannot publish agent progress. This does not prove private prompt disposal or replace the host's
memory reset fallback. See [runtime boundaries](docs/runtime-maintainers.md).

SDK 3.59.0 adds [hierarchical task/story/epic/release execution](docs/hierarchical-delivery.md), independent artifact QA and typed delivery-plan/candidate/recovery operations while preserving published V1 contracts.

See [Authoring agents under the operating contract](docs/agent-operating-contract.md) for role-policy profiles, exact model-tool exposure, typed attention checkpoints, and memory authority boundaries.

`CSweet.Agent.SDK` 4.5.0 is the supported .NET 10 authoring API for C-Sweet agents and service
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
  --SdkVersion 4.5.0
cd ResearchAgent
dotnet test
```

To author without the template, add the package directly:

```powershell
dotnet add package CSweet.Agent.SDK --version 4.5.0
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

See [agent collaboration](docs/collaboration.md) for typed documentation requests, read sharing, clarification, review, exact-revision handoffs, and durable dependency waits in SDK 3.46.1.

## Acknowledged inference waits

SDK 3.46.1 uses negotiated, lease-bound inference polling so acknowledged waiting does not consume the agent execution budget. See [LLM queue and deadlines](docs/llm-queue.md) for states, cancellation, ownership, runtime limits, and deployment requirements.

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
## Compute (3.46.1)

Use the [typed compute client](docs/compute.md) through context.Platform.Compute for Linux/Windows environment requests, lifecycle operations, guest commands, port publication, results, and durable change events. The application owns host setup and UAC; grants remain enforced by the broker.


### Task review and merge preference clients

SDK 3.50.0 adds `PreparePersonalGitWorkspaceRequest.TaskItemId` and `PlatformSourceControlClient` methods to submit/read/discover task reviews, report exact-commit QA, decide a merge from retained manager dialogue, and read/change story or epic merge preferences. `TaskDeliveryCapabilities` identifies capabilities and durable wake events. These APIs require current platform grants; events never authorize a merge. Scope changes carry the actual source message and expected revision.


### Project prerequisites (3.51.0)

Set `rolePolicy.requiresProject` to `true` for any contributor role whose delivery work needs an active project and explicit membership. The default is `false` for existing packages. This declares a prerequisite; it gives no project-creation, membership-management, or hiring authority.

Use `context.Platform.Projects` for durable intake, eligible-project discovery, human choices, setup links, readiness reads, and typed manager assistance. Read current state after `ProjectIntakeCapabilities.Changed` and discover pending requests on reconnect. See [the project policy](docs/manifest-reference.md#project-requirement) and [authoring guidance](AGENT_AUTHORING.md) for enforcement and recovery rules.


## Manager base type

Declare `rolePolicy.baseType: "manager"` with `profile: "manager.v1"` and derive from
`CSweetManagerAgentBase`. Product management and game production are specialized manager roles.
See [manager authoring](docs/manager-agents.md) for shared monitoring, assessment, recovery hooks and required grants.

`HiringAutonomyContracts` defines per-Chief owner hiring preferences, publisher selection, and authenticated decision capture and delegated submission. Platform services enforce the current policy and approved-plan binding; memory cannot grant hiring authority.

## Reviewed runtime public internet

SDK 4.5.0 defines `RuntimePermissions.PublicInternet` (`runtime.internet.public.v1`). Declare it in `requires` with a purpose and `modelVisible: false`. It requests an execution-environment permission, not a callable MCP operation or provider binding. The installed manifest and owner-approved grant must both include it. It does not imply brokered web, credentials, private-network, inbound, compute or deployment authority.

The certified development guest supplies Node/npm. Scoped project restores and portable tools may use the workload's writable directories when internet is approved; missing lockfiles are generated and verified through a clean restore. Workspace publication excludes `node_modules` and `.venv` while preserving lockfiles. Runtime-state reads report the effective network mode and expected development tools; command results remain the evidence that a particular validation ran.

## Manager blocker reports

SDK 4.5.0 exposes shared evidence-based incident reports and human-selected recovery through `CSweetManagerAgentBase` and `ManagerIncidentCoordinator`. See [manager agents](docs/manager-agents.md) for hooks, reviewed grants, bounded analysis, durable decisions and rollout.

## Bounded task technical review (4.5.0)

The SDK attaches validated source/target commits and page offsets to model findings. Models
return review content without reproducing identifiers; reversed or invented model identity
fields cannot redirect review evidence. Publication freshness, complete diff coverage and
unresolved-finding checks remain enforced before trusted integration and independent QA.

`DeliveryTaskTechnicalReview` starts with a publication/PR/branch reference and exact source/target commits, not a full patch in a model message. It reads contiguous diff pages through the existing authorized `git.merge.review.v1` capability, binding every page to the publication and patch digest. It refuses changed candidates, missing pages, oversized synthesis and approval that erases unresolved findings. Newly added or modified generated/dependency paths request source cleanup without model evaluation of their contents.

Coding agents use `RepositorySourcePolicy.Instructions` to maintain `.gitignore`, preserve manifests/lockfiles and authored assets, and build deployment output through reproducible recipes. The trusted publisher applies Git's native root/nested ignore rules to new files in snapshot workspaces; already tracked files require explicit cleanup. Ship the platform broker and trusted Git publisher before installing SDK 4.5.0 consumers.
