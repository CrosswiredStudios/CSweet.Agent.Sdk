# Security Policy

SDK 3.41.0 adds protocol-2.3 exact `If-Match` preconditions for non-media mutations.
Hosts must freeze the declared required input into the approved plan, validate one bounded strong
entity tag, and inject only that header on the exact PUT/PATCH/DELETE request. Never inherit the
condition on ownership reads, allow arbitrary headers or replace the version after approval.
A received HTTP 412 for a conditional request blocks that plan as `resource_changed`; it does not
authorize another attempt. Lost responses remain indeterminate. Older hosts must reject protocol 2.3.

SDK 3.37.0 adds protocol-2.2 response resource bindings. Hosts must compare every declared response
owner to the frozen confirmed resource before secret extraction, persistence or runtime delivery.
Malformed or mixed-owner results are withheld; uncertain mutations remain indeterminate, not retried.
Older hosts must reject protocol 2.2. See [connector contracts](docs/connectors.md).

Media protocol declarations are reviewed mapping data. `resumable-range.v1` never
allows a runtime to submit an upload URL, headers or byte offset. A host must bind
initiation and every resumed chunk to the same approved plan, exact account and
media digest. Persist opaque sessions, reconcile server offsets after interruption,
and reject malformed ranges rather than assuming bytes were received.

The SDK 3.35.0 connector-action client exposes durable request/read controls, not an authenticated
HTTP or execution escape hatch. Hosts must enforce both control and provider-operation grants,
exact-package account bindings, current authority, exact approvals and result ownership. Changed
events are wake hints, not authority. Never release results after their bound authority is revoked,
and never automatically retry an action whose external outcome is uncertain.

Please report suspected vulnerabilities privately to the repository maintainers. Do not include
credentials, production data, or working exploits in public issues.

## Event identity boundary

SDK 3.35.0 carries retained conversation provenance on media action requests through
`RequestConnectorAction.MediaSource`. Attachment metadata may expose `MediaAssetId`, never bytes or
storage paths. Neither value grants access. Hosts must require current employee membership and chat
read permission, validate the exact organization/conversation/message/attachment/asset relationship,
and freeze the source and checksum into the approved plan. Recheck that relationship before every
media exchange and result disclosure; reject missing sources, revoked membership, changed metadata
and older media plans without provenance. Non-media requests omit the optional field for compatibility.

The runtime treats `AgentEventEnvelope.EventId` as authenticated platform metadata. It is the
stable identity of the originating domain event and is distinct from the delivery `WorkId`.
Agents must use `EventId` for domain idempotency and must not accept an event identity from model
output or event payload data. The SDK rejects event work when the platform omits or empties this
identity; lifecycle acknowledgements derive it directly from the authenticated envelope.

## Source-control boundary

Git workspace v2 is assignment-scoped. Agent requests contain the work item, authoritative
assignment revision, and an idempotency key; they cannot select a connection, repository, remote,
ref, base branch, or ticket branch. Core reauthorizes every operation and delegates authenticated
provider work to a trusted GitHost service.

Agent containers receive a credential-free working tree without `.git`, remotes, provider tokens,
or installation identifiers. They may build and test that tree inside their existing sandbox, but
cannot perform authenticated Git operations. GitHost never executes builds, tests, hooks, filters,
or repository code. The SDK contains no credential or local-Git fallback when GitHost is
unavailable.

Merge review and authorization bind the team lead's decision to the exact publication and commit
SHA. A changed SHA, assignment revision, policy revision, team lead, or expired grant must fail
closed and require a new review.

## Provider connection and setup boundary

Protocol-v2 connection declarations contain only public provider profile names, approved HTTPS
origins, and named permission sets. OAuth clients, secrets, tokens, redirect endpoints, state,
PKCE verifiers, and refresh behavior are platform concerns and must never be implemented by or
disclosed to agent code.

Plugin setup is declarative. The SDK accepts only the platform-owned step kinds documented in the
manifest reference and validates all connection, permission-set, configuration, capability, and
flow references. It intentionally has no extension point for HTML, JavaScript, Razor, iframes,
remote UI, arbitrary redirects, or executable expressions. Runtime hosts must independently
enforce bootstrap capability isolation and treat every manifest value as untrusted input.

## Infrastructure provider boundary

Manifest-v2 infrastructure declarations are optional authority ceilings. A runtime may invoke only
the exact MCP tool, typed provider command, or confined file-transfer operation declared by the
installed package. Agents never receive raw MCP sessions, OAuth tokens, API credentials, SFTP
passwords, or generic HTTP/SSH access. Every provider write must be represented by a canonical
change set and an unexpired approval bound to the exact serialized payload hash.

Provider responses must be size bounded and sanitized before reaching model context, artifacts,
logs, traces, or errors. Hosted checkout URLs containing consent tokens remain in platform-owned
encrypted storage and are exposed to an authorized human only through an opaque, expiring action.
Ambiguous write failures require state reconciliation before retry, preventing duplicate purchases
or renewals. File-transfer brokers must enforce the declared host, port, root path, operation,
content digest, and an administrator-approved SSH host-key fingerprint.

## Configuration ownership boundary

The signed manifest is the authoritative settings schema. Defaults and employee overrides belong
to the trusted control plane; opening or saving settings must not invoke agent code or create a
runtime. Runtime snapshots and durable refresh messages are authenticated platform data. The SDK
accepts only monotonic revisions, swaps settings atomically, and exposes a read-only authoring
snapshot. Agents have no API for writing control-plane configuration.

## Runtime transport availability boundary

The private authenticated broker transport bounds every HTTP exchange. Control requests fail
after 30 seconds and capability calls fail after three minutes, allowing the runtime worker to
cancel affected work and reconnect instead of retaining an expired session indefinitely. Agent
code cannot disable these limits or access the underlying transport.

Planning revisions may now bind stage assignments under the existing scoped planning grant. The host validates stage policy, delegation requirements, active eligible ownership, team/profile evidence and optimistic revisions; executing planning remains immutable. Omitting assignments preserves existing ownership.

## Connector boundary (protocol 2.1)

Connector dependencies, public OAuth metadata and closed HTTP mappings require a
2.1-enforcing host. Packages never inherit trust from a claimed publisher/profile
name. Administrator approval binds an immutable build and its reviewed vault
profile. Consumer grants, provider consent and resource ownership are independent.
No raw authenticated HTTP or ordinary provider dispatch may bypass frozen-plan
approval. Host credential injection follows exact request validation, including
resumed media chunks. Secret-bearing responses fail closed before runtime delivery.
See [connector contracts](docs/connectors.md). Contract tests do not establish that
a deployment has implemented these enforcement responsibilities.

The optional `conversation.v1` setup assistance profile is a separate agent-only
authority boundary. Hosts must bind both inbound work and outgoing platform calls
to the durable protected conversation, reject unrelated data/media/tool access,
and recheck setup state when queued work is claimed. It never grants a connector
model access or lets the agent activate itself. See the setup section in
[connector contracts](docs/connectors.md).

Connector account-selection projections accept bounded JSON pointers only. Bootstrap
HTTP stays in the host, rechecking the exact installation, current step, package/profile
approval, grants and scopes before and after reads. Browser labels cannot replace
provider-returned account identity. Account lists with duplicate IDs or unfinished
pagination must not be silently accepted.


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

Connector action cancellation is a separate explicit grant and applies only to the authenticated
requesting installation. Hosts must fence cancellation against execution with a durable conditional
transition. Never report an executing or uncertain mutation as cancelled, or use cancellation to
justify resending under a new key. Decision feedback is bounded data; it cannot amend a frozen plan
or convey new authority. Return feedback only after revalidating the original result-access boundary.

SDK 3.35.0 uses negotiated, lease-bound inference polling so acknowledged waiting does not consume the agent execution budget. See [LLM queue and deadlines](docs/llm-queue.md) for states, cancellation, ownership, runtime limits, and deployment requirements.

The typed Work.DecideApprovalStageAsync client submits a scoped board-manager decision through the broker. The host binds the current waiting stage and active sprint to the assigned manager and enforces idempotent replay; the client grants no approval authority.
