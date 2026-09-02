# Durable agendas, chat intake, and structured interactions

Use this guide for agents that must continue work across turns, manage their own obligations, or
receive messages from both humans and agents. It complements the
[operating contract](agent-operating-contract.md) and
[capability and event guide](capabilities-and-events.md).

## The runtime may sleep; the obligation may not disappear

Do not keep an agent productive with an unbounded callback, sleeping loop, retained model context,
or an assumption that `InProgress` schedules another turn. Persist every future obligation in one
of the platform's durable mechanisms:

- personal work for agent-owned obligations;
- project-board work for delegated delivery;
- operating state for the agent's assessment and correlations;
- coordination sessions for bounded multi-agent exchanges;
- decisions for binding choices;
- artifacts and packages for revisioned deliverables and evidence.

Use attention review as a recovery and fairness safety net. Prefer exact platform events for known
dependencies because they provide lower latency and stronger correlation.

## Run to quiescence

On each startup, recovery, periodic review, message, or domain event:

1. Validate the event and authenticated work context.
2. Read authoritative state again; memory and previous model output are not workflow authority.
3. Reconcile deterministic agenda cards using stable domain correlations.
4. Claim the highest-priority eligible personal card.
5. Perform one bounded, cancellation-aware action.
6. Persist every external effect with a stable idempotency key.
7. Complete, defer, retain, or block the card explicitly.
8. Continue only within a fixed card, time, model-call, and mutation budget.
9. Leave remaining eligible work Ready for the next durable wake.

A personal card should be a bounded unit of work, not an entire project phase. A callback that
exhausts its execution budget must checkpoint progress and expose a deterministic successor; it
must not depend on its process staying alive.

## Personal-work dispositions

Override `HandlePersonalTodoAsync` and return exactly one `PersonalTodoResult`:

| Result | Durable effect | Use when |
| --- | --- | --- |
| `Completed(summary)` | Completes the claimed card | All required effects and evidence succeeded |
| `WaitingUntil(time, reason, person)` | Keeps the card in Doing, releases the claim, and records a scheduled review | A time-based follow-up or dependency needs a safety-net review |
| `InProgress(summary)` | Keeps the card in Doing and releases the claim without scheduling a review | A known external event will requeue the exact card |
| `Blocked(reason)` | Moves the card to Blocked and records the reason | Explicit intervention or new authority is required |

`InProgress` does **not** mean “call this callback again.” Before returning it, persist the event
type and aggregate correlation that will cause `RequeueAsync`. If no such event is guaranteed, use
`WaitingUntil` or expose an immediately Ready successor.

Request only the capabilities used by the implementation. A self-executing personal agenda commonly
needs read, add, requeue, claim, complete, block, release, and defer. Subscribe to
`com.csweet.work.personal-todo.available.v1`. The SDK privately owns claim IDs, claim expiration,
and terminal transitions; agent code must not manufacture those values.

Use `PersonalTodoItem.CorrelationId` for non-secret domain lineage. A useful correlation combines a
stable card type with its authoritative aggregate, for example:

```text
creative-review.v1:{artifactRevisionId}
manager-decision-followup.v1:{decisionId}
portfolio-review.v1:{portfolioId}:{reviewDate}
```

Before adding a card, check whether the same type and aggregate already exists. Repeated events must
update or requeue the existing obligation instead of creating duplicates.

## Waiting and event correlation

Waiting state should identify:

- expected event type;
- expected aggregate ID;
- exact revision, digest, or version when relevant;
- workstream, board, and work-item context;
- waiting person or accountable role when relevant;
- next review time and escalation policy;
- source event, causation, and idempotency identities.

An artifact decision for revision B must not wake a card waiting on revision A. A manager response
must not complete an obligation for a different conversation or superseded decision. Correlation is
part of authorization and correctness, not merely observability.

Event handlers should be thin:

1. validate and deserialize;
2. merge the authoritative fact;
3. find affected cards by correlation;
4. requeue or create cards idempotently;
5. run bounded reconciliation when appropriate;
6. return without retaining an in-memory wait.

## Chat is an input channel, not automatically a task

Route an authenticated incoming message before applying role- or phase-specific behavior. A useful
bounded disposition set is:

- answer with information;
- acknowledge or confirm;
- update or requeue an existing obligation;
- create one new personal obligation;
- respond to a structured interaction;
- request structured clarification;
- escalate, decline, or redirect;
- ignore an exact duplicate after recording its prior disposition.

Create personal work only when the request establishes a durable agent-owned obligation that cannot
be completed safely in the current bounded turn. Do not create cards for greetings, acknowledgements,
status queries, immediate bounded answers, or facts that merely complete an existing waiting card.

Determine routing from authenticated metadata before using a model:

- sender and participant identities;
- employee type, role, and reporting relationship;
- workstream, board, work item, team, and conversation context;
- exact artifact, decision, coordination, or agenda correlation;
- granted authority and role accountability.

For unstructured text, a model may return a constrained classification such as `Informational`,
`Question`, `ActionRequest`, `DecisionRequest`, or `EvidenceSubmission`. The model must not select
authority, accept evidence, or mutate workflow. Low-confidence material requests should produce
structured clarification rather than guessed work.

When a message creates or changes work, confirm what happened: card created or updated, project
context, next action, and required input. When no task was created and ambiguity is likely, say so.

## Human questions, agent coordination, and binding decisions

Choose the mechanism from the respondent and authority requirement.

### Human multiple choice

Use `AskUserAsync` for one human-facing question with two to four mutually exclusive options and one
recommendation. The UI provides the answer operation and starts a new durable agent chat turn. The
agent does not need a complementary MCP answer tool for the human widget.

Correlate the returned answer to the original decision when metadata is available. Parsing rendered
text such as “selected option” is a compatibility fallback, not the preferred contract.

### Direct agent message

A direct message persists the message and starts the recipient agent's turn. Use it for status,
information, confirmation, and low-risk bounded requests with unambiguous authenticated context.
The receiving agent still applies its chat intake router and may answer without creating work.
When the exchange concerns a project, use the `SendDirectMessageAsync` overload that supplies an
`AgentWorkContext`. A recipient should reject state mutation when a direct chat cannot be matched to
the authoritative workstream; prose mentioning a project name is not sufficient correlation.

### Durable agent coordination

Use coordination for multi-turn requests, exact evidence, work-item support, or anything that may
block delivery. Coordination provides exact participants, objective, success criteria, transcript,
work context, revision-checked responses, optional typed artifacts, and explicit `Continue`,
`Completed`, or `Blocked` dispositions.

Do not use human-facing widgets as an agent-to-agent protocol. For a non-binding structured choice,
attach a typed choice-request artifact and require a typed answer artifact that echoes the request
and context digest. For a binding choice, use the platform decision capabilities and applicable
authority envelope.

### Semantic widget interactions

If a product introduces widgets addressed to agents, expose semantic capabilities such as
`CreateInteraction` and `RespondToInteraction`. The response should name the interaction ID,
expected revision, selected action or typed payload, evidence, and idempotency key. Enforce target
identity, schema, authority, expiry, and supersession. Agents must not automate visual controls or
simulate clicks; the widget is a UI projection of a typed protocol.

## Failure and recovery checklist

Assume at-least-once delivery and termination between any two awaits.

- Use stable domain idempotency keys for every external mutation.
- Treat capability denial, conflict, unavailable providers, and approval requirements as expected
  workflow outcomes.
- On a lost response, reconcile whether the resource already exists before creating another.
- Recover expired personal claims and stranded Ready work through the platform queue.
- Preserve accepted inputs across model retries; never make a manager re-enter durable direction.
- Bind approvals and evidence to exact revisions and digests.
- Never advance a lifecycle from prose, an acknowledgement, or model self-report alone.
- Suppress unchanged notifications with durable fingerprints.
- Honor cancellation before expensive work and every mutation.

## Recommended tests

Test the operating behavior, not only helper methods:

1. A Ready card wakes and is claimed once.
2. One wake observes its card and time budget.
3. Completion creates at most one deterministic successor.
4. A deferred card wakes after restart at its review time.
5. A matching domain event requeues an in-progress card.
6. An unrelated or superseded event does not wake it.
7. Expired claims retry safely.
8. Duplicate events do not duplicate cards, messages, artifacts, or decisions.
9. A status question answers without a phantom task.
10. An action request creates one correlated task and confirms it.
11. A response updates existing work instead of creating another card.
12. Unauthorized senders cannot mutate role-owned state.
13. Human widget answers return through a new durable turn.
14. Agent choices use coordination or decisions rather than UI automation.
15. Runtime recovery does not replay completed effects.

Use `AgentTestRuntime` for callback and capability behavior. Keep manifest tests synchronized with
every required personal-work capability and event subscription.
