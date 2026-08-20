# Security Policy

Please report suspected vulnerabilities privately to the repository maintainers. Do not include
credentials, production data, or working exploits in public issues.

## Event identity boundary

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
