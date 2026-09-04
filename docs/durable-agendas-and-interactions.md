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

### Separate accepted direction, generation, and effects

Do not make one replayable chat callback implicitly own every stage of a creative or analytical
workflow. Persist the stages that have different retry semantics:

1. Route the message and persist accepted preferences, approvals, brief facts, and correlations.
2. Create or update the durable obligation that owns any slow work.
3. Generate and validate the model result within a bounded call and retry budget.
4. Checkpoint validated output in an appropriate platform-owned durable record when recreating it
   would be expensive or nondeterministic.
5. Persist each artifact, message, or work mutation with a stable idempotency key.
6. Announce completion only after required effects and returned aggregate IDs are durable.

A failure at step 5 resumes step 5 whenever the generated output is still available or was durably
checkpointed; it must not silently rerun step 3. Never use process memory or local disk as a
cross-callback checkpoint. If an optional persistence effect is denied, the agent may return the
useful generated result while explicitly saying it was not saved. If required persistence cannot
accept the first durable checkpoint, block with the precise missing authority and suppress automatic
generation retries until that dependency changes. Never discard the already accepted direction or
ask the user to enter it again.

### Persist promised follow-up before confirming it

When a terminal event unlocks a slow or multi-step follow-up, create the successor personal card
before telling the user that work is underway. The event callback should validate and store the
authoritative fact, add one idempotent correlated card, and then send a concise confirmation that
names the new work. The claimed card—not the event callback—should own the external proposal,
document generation, research, or coordination effect.

This ordering prevents a particularly confusing failure mode: chat says “I’m moving on to X,” but
an exception occurs before X has either a board card or a durable result. It also gives retries one
clear owner. Complete the successor card only after its external effect and returned aggregate ID
are persisted; defer transient failures and block missing authority with an actionable reason.

Contract-test every deterministic capability payload against the same schema enforced by the MCP
tool boundary. In-memory capability mocks prove orchestration but may not enforce string lengths,
formats, paired fields, or array limits. At minimum, fixture tests should cover every constrained
field and a platform integration test should submit the representative payload. Treat schema and
validation rejection as actionable blocked work, not a temporary dependency outage.

When a personal card is scheduled internally rather than caused by a chat message, omit both source
IDs. If it is caused by chat, provide both the authenticated conversation ID and source message ID;
never provide only one member of the pair.

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

Onboarding answers and collaboration preferences deserve an explicit state-only route. Record the
choice, acknowledge it, and stop unless the same message contains a separate action request or a
previously persisted obligation is now eligible. Likewise, a genre, constraint, reference, or
approval that completes an existing brief should update that correlated obligation instead of
creating unrelated work. This prevents a phase handler from mistaking control-plane conversation
for permission to generate a deliverable.

When a message creates or changes work, confirm what happened: card created or updated, project
context, next action, and required input. When no task was created and ambiguity is likely, say so.

### Acknowledge long-running work immediately

When an interactive request will spend noticeable time generating a document, calling tools, or
validating a deliverable, acknowledge the understood request before the slow work begins. Publish
that acknowledgement as provisional draft output and flush it immediately:

```csharp
await stream.WriteDraftAsync(
    "I can work with that. I’m starting the first draft now.", cancellationToken);
await stream.FlushAsync(cancellationToken);

var finalAnswer = await CreateAndValidateDeliverableAsync(cancellationToken);
await stream.CommitAsync(finalAnswer, cancellationToken);
```

`CommitAsync` remains the sole authoritative response and replaces the provisional draft in the
conversation. Do not send the acknowledgement as a separate direct message: that creates duplicate
durable chat entries and may accidentally trigger another agent turn. Keep the acknowledgement
specific enough to confirm what will happen next, but do not claim completion before the evidence
or artifact exists.

## Human questions, agent coordination, and binding decisions

Choose the mechanism from the respondent and authority requirement.

### Human multiple choice

Use `AskUserAsync` for one human-facing question with two to four mutually exclusive options and one
recommendation. The UI provides the answer operation and starts a new durable agent chat turn. The
agent does not need a complementary MCP answer tool for the human widget.

Correlate the returned answer to the original decision when metadata is available. Parsing rendered
text such as “selected option” is a compatibility fallback, not the preferred contract.

### Revisioned document review

Treat the artifact revision as the single source of truth for document approval. Do not create an
`AskUserAsync` choice card for **Accept** or **Request changes** when the document workspace already
offers those decisions. Two independently persisted controls create synchronization, replay, and
supersession problems.

A chat attachment may offer a convenience action such as **Approve linked revision**, but that
action must call the same artifact-decision capability as the document workspace and name the exact
attached revision. It is a shortcut into the canonical workflow, not a second decision record.
Requesting changes should remain in the document workspace when feedback is required.

After a human decides an agent-created revision, the platform should emit the typed exact-revision
decision event to the creating or responsible agent. The agent verifies artifact ID, revision ID,
digest, terminal status, and project/conversation correlation before advancing its agenda. Duplicate
delivery must be harmless, and unrelated or superseded revision events must not wake the card.

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
- Resume the failed stage; do not repeat completed model generation because a later mutation failed.
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
16. Long-running interactive work flushes a provisional acknowledgement before the slow operation,
    and the final commit replaces it without creating a duplicate message.
17. Document review has one canonical decision record; viewer actions and chat shortcuts target the
    same exact revision, and the resulting event advances only the correlated agenda item.
18. A terminal decision that unlocks slow follow-up creates its successor card before confirmation;
    the claimed card owns the effect, and injected failure cannot leave a promise without visible work.
19. A preference-only onboarding turn persists the choice and does not start deliverable generation.
20. Empty or incomplete model output cannot create an artifact or advance the lifecycle.
21. An injected artifact failure retries persistence without repeating completed model generation.
22. A scoped authorization denial retains prior stage output and produces an actionable blocked result.

Use `AgentTestRuntime` for callback and capability behavior. Keep manifest tests synchronized with
every required personal-work capability and event subscription.
