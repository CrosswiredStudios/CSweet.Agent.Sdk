# Reusable agent collaboration (SDK 3.35.0)

Use `CollaborationActions` with `Platform.Communication` for documentation requests,
clarification, review, and handoff. These are typed artifacts carried by the existing durable
coordination service, not a new transport or a source of authority. Domain agents still decide
what is sufficient, which questions matter, and how to staff their work.

## Request and provide documentation

`RequestDocumentation(key, DocumentationRequest)` asks the addressed participant to supply or
create documentation. Include purpose, document type, required content, acceptance criteria,
optional due date, and explicit source references. Due dates communicate expectations; they do
not automatically schedule or escalate work. The recipient reads or creates the document using
`Artifacts.ListAsync`, `GetAsync`, and `CreateAsync`, then shares the result. Asking for a document
does not itself create one or assign a separate personal work item.

```csharp
var artifact = CollaborationActions.RequestDocumentation("project-42:requirements:v1",
    new DocumentationRequest("Prepare an implementation plan", "requirements.v1",
        ["Scope", "Constraints"], ["Acceptance criteria are testable"], []));
var request = new StartBoardCoordinationRequest(producerId, boardId,
    "Requirements", "Agree on sufficient implementation context",
    ["Questions resolved", "Exact document revision reviewed"],
    "Please provide the requirements and identify missing decisions.",
    "project-42:requirements-session", artifact);
await context.Platform.Communication.StartBoardCoordinationAsync(request, token);
```

The same artifact can be passed to `StartCoordinationAsync`, `StartWorkItemCoordinationAsync`,
`RespondToCoordinationAsync`, or the artifact parameter of a coordination callback result.
Use the entry point appropriate to the existing chat, work assignment, or team board. Each
retains its participant authorization requirements. Do not invent source identifiers.

## Share a document versus request access

`ShareDocuments(key, references)` shares explicitly named documents with the other authenticated
participant. `WithDocuments(domainArtifact, references)` adds the same typed attachments to an
existing domain-specific payload. A `CollaborationDocumentReference` contains document ID,
revision ID, and content SHA-256. At most eight distinct documents may be shared in one action.

Core verifies the author is the document creator or steward, has a current exact-document read
grant, and references a real revision and hash in the same organization. All references are
validated before any grant is added. The recipient receives **document-level read access**,
including other revisions readable through that document; this is not revision-only access.
Sharing does not grant revise, submit, decide, package approval, or onward-sharing authority.
Repeated delivery does not duplicate existing grants. Grants have no automatic expiration.

If the caller cannot share a source, use the existing
`Artifacts.RequestAccessAsync(new RequestArtifactAccess(documentId, ["artifact.read"], justification, key))`.
An access request is pending until approved; it is not permission to proceed. Collaborative
editing similarly requires explicit `artifact.revise` authority. Use `ReviseAsync` with the expected base
revision and handle conflicts; never silently overwrite another author's edits. For an exact
approved source use `Artifacts.ReadAcceptedAsync(reference, token)`, which checks accepted status,
revision identity and hash even when the document has newer revisions.

## Clarification, review, and handoff

* `RequestClarification` carries stable question IDs and optional source references.
* `AnswerClarification` validates answers against those IDs. Partial answers are allowed; the
  recipient must keep unanswered questions open. Duplicate/unknown answer IDs are rejected.
* `RequestReview` identifies one exact revision and acceptance criteria. Submit the revision via
  `Artifacts.SubmitAsync` to establish formal document review; a conversation alone does not.
* `ReviewDecision` records acceptance or requested changes. It cannot accept while requiring
  changes. Record formal approval separately with the grant-governed `Artifacts.DecideAsync`.
* `Handoff` states the recipient's readiness, open questions, blockers, rationale and exact
  document revision. `CanAcceptHandoff` checks readiness and matching review of that revision.
  A newer revision requires a new review. Neither helper grants formal document approval.

Review decisions and handoffs reference documents without re-sharing them, so a reader does not
need owner/steward authority to reply. `Read<T>(artifact, expectedType)` checks type and schema
version before deserialization. Always select artifacts from the expected authenticated speaker
using the coordination transcript, bind them to the expected request/session and exact revision,
and validate their contents. They remain participant-authored data, not trusted authorization.

The game agents retain pitch-specific content and wire types, while using the SDK's document
references, sharing helper, accepted-source lookup and common readiness/acceptance checks.

## Wait without stopping independent work

In a personal to-do callback, read authoritative dependency state and pass observations to
`CollaborationDependencies.WaitFor`. It returns null when every dependency is satisfied, or a
`PersonalTodoResult` that defers only this item through the existing durable scheduler:

```csharp
var waiting = CollaborationDependencies.WaitFor(
    [new CollaborationDependency("requirements-review", requirementsAccepted,
        "Waiting for the requirements review", reviewerId)],
    DateTimeOffset.UtcNow.AddMinutes(5));
if (waiting is not null) return waiting;
// Continue this item now that its dependencies are satisfied.
```

This is a timed recheck, not a new event-subscription service. Persist enough identifiers in the
work item or operating state to re-read the dependency on the next callback. Do not persist an
observed `Satisfied=true` as permanent truth. Where an existing accepted-document, access, or
workforce event is available, its handler can requeue the dependent personal item through the
existing typed API for earlier progress. Other ready work remains independently runnable.

## Track a collaboration with a card

A collaboration that carries real work needs an owner the business can see. Start it, then give it a
card on your own agenda:

```csharp
var session = await context.Platform.Communication.StartBoardCoordinationAsync(request, cancellationToken);
await TrackedCollaboration.QueueCardAsync(context, session,
    new TrackedCollaborationCard("Get the project proposal approved", "Tracks the Producer's proposal.")
    {
        TeamId = teamId
    }, cancellationToken);
```

The card is bound to the exact session and, for a board collaboration, to that board, so it appears
in the board's leadership work and the platform can resume the collaboration through it. Adding is
idempotent per session. In `HandlePersonalTodoAsync`, answer the card from the session:

```csharp
if (TrackedCollaboration.IsTrackingCard(item))
    return await TrackedCollaboration.ReviewAsync(context, item,
        new TrackedCollaborationReview(OutcomeReached: state.ProjectId.HasValue)
        {
            CurrentSessionId = state.ProposalSessionId
        }, cancellationToken);
```

`OutcomeReached` is your own durable fact, such as "the project exists". A completed session is not
the outcome by itself. A stopped session blocks the card with a `CollaborationBlocker`: a reason
code, who owns the next step, what must change, and whether a retry is useful. Agents do not ask a
model for any of these.

In tests, call `runtime.AssertCollaborationsTracked()` after the callbacks under test. It fails when
a chat or board collaboration was started without an owning card. Work-item collaborations are owned
by their ticket and are not checked.

## Permissions, retries, and migration

Declare the existing communication coordination, artifact read/create/revise/submit/decide or
request-access capabilities actually used. Personal work needs the existing personal-todo
capabilities. There are no new blanket collaboration grants; manifest requests never grant
authority. SDK helpers do not bypass host checks or authenticated session routing.

Use stable request keys for each logical effect and a new key for a changed revision/request.
Callbacks are delivered at least once. Cache model decisions before mutations and use expected
revisions when responding or editing. Finalization/turn limits are not implicit acceptance:
return blocked with unresolved questions and resume explicitly when new context is available.

SDK 3.35.0 is additive. Existing domain artifact payloads continue to work. Deploy the matching
Core host for sharing at board/work-item starts; updating an SDK package alone does not deploy
that server behavior. Tests exercise the examples, invalid actions, readiness, and exact-source
lookup without granting external access or invoking a live model.
