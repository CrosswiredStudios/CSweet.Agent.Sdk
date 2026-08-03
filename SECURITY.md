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
