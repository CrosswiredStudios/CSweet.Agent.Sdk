# Runtime maintainer guide

SDK 3.39.0 adds `ConnectorHttpOperation.IfMatchInput` for protocol 2.3. Its pointer must select a
required string with `maxLength` between 3 and 256. Only non-bootstrap, non-media PUT/PATCH/DELETE
mutations may declare it. Use `ConnectorEntityTag.RequireStrong` to reject wildcard, weak, list,
control-character and oversized values. Freeze the exact tag into the canonical request hash and
revalidate it with the complete plan before credential injection. Do not copy it onto preflight reads.
Only a received 412 for that conditional request establishes failed preconditions; retain a durable,
content-free `resource_changed` condition with the blocked plan. Never release provider error bodies
or retry unknown outcomes. Fresh evidence and fresh authorization are required for a revised action.

SDK 3.37.0 adds `ConnectorHttpOperation.ResponseResourcePointers`. Freeze these protocol-2.2
declarations into each prepared request and enforce them on reads, mutation results and completed
media transfers before storing or releasing content. All selected owners must equal the frozen
confirmed resource; empty arrays are valid but missing paths and mixed owners are not. Reject new
declarations on protocol 2.1 and bootstrap. Older hosts must reject minimum protocol 2.2.

SDK 3.35.0 adds `ConversationAttachmentReference` and optional `RequestConnectorAction.MediaSource`.
The host supplies `CommunicationAttachment.MediaAssetId` only as metadata of visible retained history.
Media actions must bind that asset to its exact source, not just to an organization. Validate active
employee/chat membership and the chat-read grant, retained source records, matching checksum/size/type,
then freeze provenance with the request. Revalidate before chunks and result reads. These fields do
not carry tokens or authorize provider effects. Unsupported older hosts must reject sourced requests.

SDK 3.35.0 adds the optional closed `http.mediaProtocol` declaration. Freeze its value
in request plans. A missing or unsupported protocol cannot enter a legacy raw HTTP
or media handler. Implement transfers as host-owned durable jobs, not retained agent
callbacks, and recheck authority before each credential injection and checkpoint.

SDK 3.35.0 adds hidden `platform.connector.action.request.v1` and
`platform.connector.action.read.v1` controls. Their typed client contains no transport logic or
credentials. The host owns plan preparation, exact decisions, durable execution and sanitized
results. Deliver `com.csweet.connector.action.changed.v1` only to the exact requesting installation;
the callback must reread current action state rather than trusting a stale event payload. Pending
decisions and external work must not retain an agent callback or an in-memory wait.

This document describes SDK internals. Agent authors should use the callback API and must not depend on these details.

The SDK uses private Streamable HTTP `/mcp`. `initialize` reads a one-use workload token from the configured secret file. It sends runtime, tick, installation, organization, package identity, and version metadata. The returned token is held only in memory, renewed after five minutes, and discarded on disconnect.

The worker long-polls `csweet/work/claim` for at most 25 seconds, runs bounded concurrent callbacks, renews each 60-second lease every 20 seconds, reports bounded progress, and completes/fails using the attempt and lease token. Broker control requests have a 30-second client boundary, while capability calls have a three-minute boundary so a stalled HTTP exchange cannot permanently wedge the work loop. Event leases carry both the delivery `workId` and the originating domain `eventId`; the worker rejects event work without the latter. It reconnects with bounded jitter and never invents work from notifications.

`ConfigurationUpdate` is reserved control work. The worker drains ordinary callbacks before
applying it, ignores stale revisions, atomically swaps the settings snapshot, invokes the typed
`OnConfigurationChangedAsync` hook, and acknowledges the applied revision and digest. Settings
are platform-owned: agents can read snapshots but cannot persist defaults or employee overrides.

`tools/list` is the only descriptor source. The SDK caches descriptors only by grant revision. Typed calls still resolve a live descriptor and the gateway reauthorizes every call. `modelVisible` controls conversion to `AITool`; transport and lifecycle tools are never exposed to models.

Git workspace v2 typed clients intentionally omit repository and ref selection. Do not add clone
URLs, connection IDs, provider installation IDs, credentials, branches, base refs, or arbitrary
commit SHAs to prepare requests. The authoritative assignment revision is required on every
workspace operation. `PlatformSourceControlClient` exposes only bounded repository intent and
exact-SHA decisions; it is not a general GitHub administration client.

Source-control provider authentication belongs to separately deployed trusted services. SDK code
must never inspect `.git`, inject credentials, invoke authenticated Git, mint provider tokens, or
offer a local fallback. A provider outage is reported as a bounded platform capability failure.

Runtime methods:

- `csweet/session/renew`
- `csweet/work/claim`
- `csweet/work/renew`
- `csweet/work/progress`
- `csweet/work/complete`
- `csweet/work/fail`
- `csweet/runtime/complete`

Never expose session/workload/lease tokens, the endpoint, `HttpClient`, JSON-RPC, or MCP objects through public authoring APIs. Transport interfaces remain internal. Tests use `AgentTestRuntime`, which deliberately models callbacks and capability grants rather than wire details.

Protocol extensions must be additive within 2.x, server-advertised, size-bounded, authenticated, non-model-visible, and covered by replay/restart/cancellation tests. A change to identity, authorization, lease, completion, or credential semantics requires a new protocol minimum and coordinated security review.

Manifest `connections` and `setup` are additive authoring contracts, not authority. The importing
platform must repeat validation, resolve provider profiles from its trusted registry, render only
known native components, and restrict setup callbacks to a bootstrap grant containing exactly the
declared setup capabilities. The normal agent runtime, model access, memory, chat, organization
data, filesystem, and ordinary network grants remain unavailable until platform activation gates
transition the installation to ready.

For progressive staffing, ReviseWorkItemPlanningRequest accepts optional StageAssignments and AccountableOrganizationUserId. Null assignments preserve existing ownership; explicit replacements are validated by the host against stage policy, ticket requirements, roster and profile evidence. The agent cannot use a planning revision to bypass execution immutability or eligibility checks.

## Connector protocol minimum

SDK 3.35.0 adds protocol 2.1 [connector contracts](connectors.md). The private MCP
wire version is independent. Reject enhanced manifests on older hosts. Connectors
may not use ordinary provider dispatch, raw HTTP, model tools or credential values.
Explicit dependency selection pins a package digest; refresh/reconciliation cannot
carry approval to another build or account. Each request must recheck grants,
connection state, resource ownership, frozen plan and decision/policy authorization.
Limited agent setup assistance is separate from connector bootstrap: it may reach
only designated participants, without normal external or business-data authority.
Connector `accountOptions` projections run through host-only bootstrap reads, not the
ordinary provider-work queue. The current step and immutable approval must be rechecked
around each request, and a health check must rediscover the confirmed account using the
authenticated provider rather than trust a supplied account ID. Literal bound-resource
prefixes are fixed mapping data and are included in request-plan hashing.
`setup.assistance.profile = conversation.v1` requires inbound conversation/event
binding and outgoing payload enforcement in addition to filtering the runtime
grant. Recheck at work claim and platform invocation, not only at enqueue/session
creation. Deliver the durable introduction and one 24-hour reminder with distinct
stable event identities, and keep the ordinary onboarding event behind activation.


SDK 3.35.0 adds assignment-scoped internal Git LFS locks through `context.Platform.Git.ListLocksAsync`, `LockFileAsync`, and `UnlockFileAsync`. Declare `git.workspace.locks.read.v2`, `git.workspace.locks.create.v2`, and `git.workspace.locks.release.v2` as needed (the separate `git-file-locks` capability group does not expand existing workspace grants). Core derives repository and employee ownership from the current assignment and team grant. Agents cannot choose owner identities, force another owner's unlock, or access provider credentials. Repeat acquisition of the same owned path returns the existing lock; repeat release is harmless. Own locks permit work-branch publication; release them before a governed merge. Managers can release orphaned locks. GitHub agent-owned locks are not supported by this API.

## Coordination document sharing (SDK 3.35.0)

Typed collaboration actions use existing coordination authority. At chat, board, and work-item
starts and participant replies, Core verifies creator/steward ownership, current document read
authority, organization, and exact revision/hash before granting the other authenticated
participant document-level read. This includes other revisions; it is not revision-only access.
No revise/decide/submit permission is granted by sharing. All references are validated before
grant mutation; session persistence and grants commit together. Review/handoff declarations
are not formal artifact approval. Runtime scheduling for dependency waits uses personal-to-do
deferral; no new event subscription mechanism is introduced.

## Acknowledged inference waits

Connector action cancellation must use the execution record's optimistic revision and commit its
receipt, proposal state and exact-requester wake obligation together. A competing execution claim
must win or lose atomically. Do not reclaim Executing/Indeterminate/Completed actions through cancel.
The cancellation control is hidden from model tools and independently granted. Action reads can
project decision feedback only from the same action/plan receipt after live authority validation.

SDK 3.35.0 uses negotiated, lease-bound inference polling so acknowledged waiting does not consume the agent execution budget. See [LLM queue and deadlines](llm-queue.md) for states, cancellation, ownership, runtime limits, and deployment requirements.

The typed Work.DecideApprovalStageAsync client submits a scoped board-manager decision through the broker. The host binds the current waiting stage and active sprint to the assigned manager and enforces idempotent replay; the client grants no approval authority.
