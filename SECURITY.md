# Security Policy

Please report suspected vulnerabilities privately to the repository maintainers. Do not include
credentials, production data, or working exploits in public issues.

## Event identity boundary

The runtime treats `AgentEventEnvelope.EventId` as authenticated platform metadata. It is the
stable identity of the originating domain event and is distinct from the delivery `WorkId`.
Agents must use `EventId` for domain idempotency and must not accept an event identity from model
output or event payload data. The SDK rejects event work when the platform omits or empties this
identity; lifecycle acknowledgements derive it directly from the authenticated envelope.
