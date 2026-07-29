# Migrating to SDK 2.0

SDK 2.0 separates durable delivery identity from domain-event identity.

## Event envelopes

`AgentEventEnvelope` now requires both identifiers:

```csharp
new AgentEventEnvelope(
    workId,
    eventId,
    eventType,
    data,
    occurredAt,
    correlationId);
```

- `WorkId` identifies one durable delivery attempt stream.
- `EventId` identifies the originating domain event and is stable across deliveries.

Use `EventId` for downstream idempotency. Do not derive it from `WorkId`, `CorrelationId`, or
payload content.

## Onboarding

Delete agent-local copies of `AgentOnboardedEvent`, `CompleteAgentOnboardingRequest`, and the
onboarding event-name constant. Use the canonical SDK contracts instead.

Replace manual capability invocation with:

```csharp
await context.Platform.Lifecycle.CompleteOnboardingAsync(
    message,
    cancellationToken);
```

The typed client verifies the event type and always acknowledges `message.EventId`.

## Test runtime

`AgentTestRuntime.DeliverEventAsync` accepts an optional explicit `eventId`. Supply one when a
test asserts idempotency or lifecycle behavior.
