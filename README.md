SDK 5.1.0 bounds derived workflow-ticket keys to the platform's 160-character limit. Short keys retain their existing identity; oversized keys use a stable 83-character SHA-256 identity so interrupted publication can replay without duplicate tickets.

SDK 5.0.4 runs ordinary networked npm restoration as a durable guest service with no overall installation deadline. Short compute commands launch or observe the job; runtime restarts reuse its stable identity and only a completed successful job receipt permits tests/build. A lost launch reply leads to observation rather than another install. The VM lease, explicit destruction and npm's own completion/failure remain authoritative.

# C-Sweet Agent SDK

SDK 5.7.0 adds one-operation, hash-pinned compute evidence exports and verified retained-artifact downloads within one agent callback.

SDK 5.7.0 adds the reviewed, host-owned RealtyLeadsAI API-key MCP profile for protocol 2.5. It includes the 5.5.3 runtime log-budget and 5.5.4 claim-deadline fixes.

The SDK also supports provider-neutral enterprise account-management declarations for protocol 2.4.
Plugins describe native account/resource pages, discovery, policy links and permission groups;
the host remains responsible for OAuth, account/business grants and execution enforcement.
See [connector contracts](docs/connectors.md). Declarations alone do not enable a host.

The SDK also provides `Platform.ChiefLeadership` for revision-checked focus decisions, coverage outcomes and recovery. The platform authenticates the owner and current Chief; the typed client grants no hiring authority. Deploy the companion platform schema and hidden capabilities before requiring this API.

SDK 5.3.1 adds `AgentOperatingStateKey.Create` for bounded deterministic checkpoint keys and shared operating-state validation in clients, test runtimes, and the platform. See [operating-state keys](docs/agent-operating-contract.md#operating-state-keys).

SDK 5.2.0 adds opt-in, assignment-scoped Git ancestor metadata to the existing inspection grant.
See [capabilities](docs/capabilities-and-events.md) and [runtime rollout](docs/runtime-maintainers.md).
Core selects the repository and exact source; no credentials, arbitrary refs or writes are exposed.

SDK 5.1.0 adds [platform research and private execution checkpoints](docs/research-and-checkpoints.md)
for reusable execution adapters. Research uses reviewed SearXNG/public-web capabilities; checkpoint
archives remain separate from source publication and do not confer execution authority.

SDK 5.0.4 waits for confirmed teardown after provisioning fails before selecting a new durable provisioning key, with at most three replacements before guest commands begin. Cleanup's generation change cannot masquerade as an unknown application command. Submitted guest commands retain their original replay fences and independent acceptance remains mandatory.

SDK 5.0.4 provisions an approved outbound-network compute VM for new Node readiness runs and executes ordinary `npm ci`. It does not enumerate or transfer package tarballs through the managed bridge. Source and lockfile hashes remain pinned, operation receipts remain durable, and independent QA/technical acceptance remain mandatory. Existing in-flight requests retain their original terms. Verbose test/build output stays in the guest's `validation.log`; failures return a bounded diagnostic tail and successful runs retain the exact-source HTTP marker.

SDK 5.0.4 permits one upload-only archive restart before any validation or publication starts. It preserves the previous archive hash, advances operation keys, and overwrites the transfer from offset zero. Repeated transport changes or unknown validation outcomes still stop readiness. Deploy the trusted host's deterministic ZIP serialization fix before recovering a legacy partial transfer; final readiness still verifies the exact archive and independently recorded test/link receipts.

SDK 5.0.4 reconciles compute generations as operation cursors: each accepted command or port publication advances the counter. Readiness resumes the exact saved request and receipt after interruption, persists publication terms before submission, and rejects unexplained cursor changes. Validation and publication evidence bind adjacent generations; neither a successful upload nor cursor recovery establishes application readiness.

SDK 5.0.4 divides aggregate reviews with more than two acceptance criteria into two-criterion assessments and a final whole-candidate consistency check. Exact-input checkpoints and progress survive interrupted callbacks; schema correction repeats only the affected assessment. Every criterion failure and finding remains blocking. Current authority, planning, source and evidence are checked before checkpoint reuse and before returning a verdict. Review calls request 128,000 output tokens; the platform's saved agent budget remains authoritative.

SDK 5.0.4 applies the shared three-correction JSON reader to aggregate technical and manager reviews. Validation names incorrect identities and criteria, separates acceptance criteria from checklists, and preserves real rejections. The current candidate, reviewer, planning and evidence are reauthorized before any verdict is returned. Exhaustion reports an aggregate processing failure and grants no acceptance.

SDK 5.0.4 directs progressive increments to `csweet/integration/<project-id>` after independent aggregate acceptance. Successors build from that shared branch; promotion to the repository default branch remains a separate final-release plan. Existing explicit bindings are retained. The platform preserves owner-recorded deferred release obligations across plans and checks them before final promotion.

SDK 5.0.4 adds bounded correction to exact-revision artifact QA. `ArtifactDeliveryReview` keeps acceptance criteria separate from requirement checklists, reports field-specific schema errors, allows three corrections, and rechecks current planning and document authorization before accepting a verdict. Artifact responses request 128,000 output tokens within the configured model ceiling and retain the 128,000-character response bound. Genuine findings remain failures; correction exhaustion is a C-Sweet artifact-QA processing issue for management.

Bounded task technical review allows 128,000 characters for requirements, synthesis input and each model response. Summary, evidence, resolution, finding and note fields allow 16,000 characters; repository paths allow 2,048. A response may contain 16 blockers, 16 observations and 16 questions; criterion/resolution citations may reference up to 512 reviewed offsets. The platform's configured model token ceiling remains authoritative.

The response schema carries these bounds, and validation reports all text-bound violations with JSON paths and measured lengths. Three correction calls are allowed after the initial response; each retains only the latest failed response. Invalid responses never grant approval or silently truncate accepted evidence. Exhaustion identifies a C-Sweet review-processing issue for management escalation. Existing saved page policy/identity remains compatible and every reused page is reauthorized and hash checked.

SDK 5.0.4 uses model-independent `csweet/story/` and `csweet/release/` names for new managed delivery branches. C-Sweet also uses `csweet/task/`, `csweet/epic/` and `csweet/candidate/`. Existing explicit branch bindings remain authoritative.

`HierarchicalProjectDelivery.PrepareIncrementAsync` selects fully detailed Epic increments while retaining future approved scope on the board. Each selected increment still requires task, Story, Epic and release acceptance, independent reviewers and existing preflight. An active plan cannot silently change its selected increment. See [hierarchical delivery](docs/hierarchical-delivery.md).

SDK 4.4.1 adds bounded retry of interrupted work heartbeat requests. It preserves the
same authenticated attempt and existing timeout; it does not revive expired leases
or retry arbitrary capability mutations. See `docs/runtime-maintainers.md`.

SDK 5.0.4 uses one execution capability, `work.execution.run`, and one scope-aware
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

`CSweet.Agent.SDK` 5.7.0 is the supported .NET 10 authoring API for C-Sweet agents and service
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
  --SdkVersion 5.7.0
cd ResearchAgent
dotnet test
```

To author without the template, add the package directly:

```powershell
dotnet add package CSweet.Agent.SDK --version 5.7.0
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

SDK 5.0.4 defines `RuntimePermissions.PublicInternet` (`runtime.internet.public.v1`). Declare it in `requires` with a purpose and `modelVisible: false`. It requests an execution-environment permission, not a callable MCP operation or provider binding. The installed manifest and owner-approved grant must both include it. It does not imply brokered web, credentials, private-network, inbound, compute or deployment authority.

The certified development guest supplies Node/npm. Scoped project restores and portable tools may use the workload's writable directories when internet is approved; missing lockfiles are generated and verified through a clean restore. Workspace publication excludes `node_modules` and `.venv` while preserving lockfiles. Runtime-state reads report the effective network mode and expected development tools; command results remain the evidence that a particular validation ran.

## Manager blocker reports

SDK 5.0.4 exposes shared evidence-based incident reports and human-selected recovery through `CSweetManagerAgentBase` and `ManagerIncidentCoordinator`. See [manager agents](docs/manager-agents.md) for hooks, reviewed grants, bounded analysis, durable decisions and rollout.

## Bounded task technical review (5.0.4)

`DeliveryScopeReview` uses the latest relevant gate verdicts and includes each candidate document once. Large aggregate technical reviews inspect bounded source parts through `AggregateSourceReview`, retain exact file coverage and resumable evidence, and assess criteria against those inspections. Source findings survive the final synthesis. Completed inspections are reused only for the same candidate, scope and reviewer; every model request retains the 128k output allowance.

The SDK attaches validated source/target commits and page offsets to model findings. Models
return review content without reproducing identifiers; reversed or invented model identity
fields cannot redirect review evidence. Publication freshness, complete diff coverage and
unresolved-finding checks remain enforced before trusted integration and independent QA.

`DeliveryTaskTechnicalReview` starts with a publication/PR/branch reference and exact source/target commits, not a full patch in a model message. It reads contiguous diff pages through the existing authorized `git.merge.review.v1` capability, binding every page to the publication and patch digest. It refuses changed candidates, missing pages, oversized synthesis and approval that erases unresolved findings. Newly added or modified generated/dependency paths request source cleanup without model evaluation of their contents.

Coding agents use `RepositorySourcePolicy.Instructions` to maintain `.gitignore`, preserve manifests/lockfiles and authored assets, and build deployment output through reproducible recipes. The trusted publisher applies Git's native root/nested ignore rules to new files in snapshot workspaces; already tracked files require explicit cleanup. Ship the platform broker and trusted Git publisher before installing SDK 5.0.4 consumers.
