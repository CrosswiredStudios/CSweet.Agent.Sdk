# Shared planning and delivery lifecycle

## Self-resolving public interactions (5.34.0)

`ScopedWorkRefinement` defines `work.planning.refinement.v1` requests and
`work.planning.refinement-result.v1` answers over normal source-bound board coordination.
`WorkRefinementRequest` supplies accepted source references, optional exact approved package,
available work kinds/parent relationships and named role choices. The contributor returns only
judgment: approaching work, local dependencies, verification and rationale. Code supplies all
identities, stable command keys and provenance. `PublishAsync` rechecks the accepted source and
current owner before each ordinary board mutation. Retain the same request/draft/publication key
after interruption; an accepted command with a lost response is recovered by platform idempotency.

This is additive refinement for missing approved outcomes. It preserves existing work and supports
caller-defined roles/game types. It does not authorize scope changes, staffing, spending or QA
overrides. Keep the existing staffing/readiness and protected-decision checks. Choose smaller new
Stories when useful; delivery still operates on the whole selected Story. Replacing published
structural work requires its normal governed amendment.

`WorkIssueContributions.ReviewAsync` is the optional owner-side helper for read-only contributors.
It provides exact approved text and current canonical obligations to judgment. Code binds
`ReviewedIssues`; the platform rejects stale versions. A platform conflict/validation rejection is
shown once with fresh records, then an invalid retained proposal is declined with its correction
reason. Declining a proposal does not resolve its underlying obligation or erase evidence.

Third-party agents can implement these wire contracts directly. Public tracking uses
`PersonalTodoWorkContext.Purpose = collaboration-tracking` and `CoordinationSessionId`, not private
SDK state keys or correlation prefixes. Old SDK tracking prefixes remain readable. Accepted
contributions expose private origin sessions only to their original participant for cleanup.

Use `IndependentWork.RunAsync` for genuinely independent owned continuations. It runs each branch
and then preserves the original single failure or aggregates multiple failures. It neither swallows
errors nor bypasses the affected branch's dependencies.

SDK 5.29.0 supplies mechanics without prescribing a game genre, team roster or number of sprints.

`WorkPlanningJudgments` separates model judgments from the authoritative envelope. Agents assess
effort, uncertainty and independently available capacity against their actual current commitments.
The adapter copies item identities, role, planning revision and digest in code. A retained
assessment is attributable evidence, never permission to execute. `WorkPlanningReadiness` runs
structural checks locally; it does not claim that requirements are semantically testable.

`WorkContributions` standardizes scoped advice over native coordination. A current board manager
supplies a question and exact accepted project sources. Any qualified participant can implement
the protocol while retaining its domain prompt. The result distinguishes assessment, proposed
change and evidence still needed. Advice cannot approve scope, hire staff, dismiss findings or
invent observations. Reauthorize current sources and the accountable requester after generation.

`DeliveryInputReconciliation` propagates only named accepted revision changes. The platform checks
canonical planning, preserved assignments, workflow topology and writer/promotion fences. Legacy
snapshots can carry original accepted delivery inputs explicitly; their digest must match the stored pin. Unrelated changes require their
own accountable decision. The helper never resumes an owner's paused plan. Structural changes
need a successor graph rather than a text-reconciliation command.

`DeliveryCorrectionCoordinator` retains findings and follow-up lineage. Managers can provide its
preparation callback to reuse normal estimates, capacity, staffing and sprint preflight. The
default recipe is a compatible convenience, not a platform rule. A declared acceptance sequence
is preserved in order; artifact corrections do not acquire repository requirements. Explicit
plural source bindings let managers declare corrective work spanning several accepted inputs.
Current task/plan receipts, independent reviews where declared, and fresh acceptance resolve
findings; a report or cancellation cannot do so.

`FinalProductDelivery` creates a visible final workflow after incremental acceptance. The caller
chooses the approved obligations, accepted inputs, checks, assignments and repository targets.
Final verification is distinct from an implementation sprint. It must not create an empty next
sprint. Successful correction lineage can replace a rejected final candidate while retaining its
history. Reporting has a separate durable obligation and cannot block authoritative execution.

`Platform.CompleteWorkstreamDeliveryAsync` invokes the explicitly granted, model-hidden
`platform.workstream.delivery.complete.v1`. The current accountable manager supplies the exact
completed final plan, scope revision and accepted source. The platform validates current evidence,
remaining project work and authority, then commits completion, receipt and wake together. The
operation records verified delivery; it cannot edit the approved scope, advance launch gates or
publish a product. Public launch continues to require its own actual authorization and executor.

All events are wake hints. Read current authorized state on reconnect and make mutations
idempotent. `DurableWorkHandoff` checkpoints input-bound judgments or idempotent domain handoffs;
external effects still require their own domain receipts.
# Staffing corrections (5.29.0)

Resource-change reads include authorized pending `Corrections` independently of recent request
history. Treat their event as a wake hint and rediscover on reconnect. An unresolved correction
identifies the approved source, disputed slot, accountable author, owner direction and revision;
it does not itself grant hiring or spending authority.

`ReadResourceChangeContextAsync` resolves each pending correction's exact approved source and current
replacement using authorized targeted reads when they have fallen outside the recent-request page.
Filtered `ReadResourceChangesAsync` calls retain their existing semantics.

Staffing-author agents should keep a durable correction commitment, reassess work against the
actual roster, retain occupied positions, and submit a replacement using `CorrectionId` and the
current approved source. Include the exact direction as `ResourceChangeEvidence` with kind
`staffing-correction`, source revision `<correction ID in N format>:<revision>`, and summary equal
to its reason. Bind identity/evidence in code; the model chooses capability needs and work.

The platform validates the binding and preserves fulfilled staffing. Normal manager approval
completes the correction and supersedes the source; normal hiring policy fulfills the replacement.
Do not recreate the cancelled recommendation or treat a proposal as an installed team member.
New manager decisions may release the specific correction wait after a current-state read;
unrelated notifications must not bypass a retry deadline or reopen an unchanged terminal block.

## Meaningful ownership and scoped reconciliation (5.30.0)

`PlanningOwnershipEvidence` retains code-supplied identity, kind, exact text and scope. A manager
supplies the candidate catalog; the model judges semantics and selects indexes. `BindEvidence`
rejects invented references. Outcomes, criteria, constraints and authoritative decisions can own
obligations without copying every rule into every criterion. A binding is planning responsibility,
not proof of implementation, authority change or test success.

`PlanningScopeOwnership.BlockedScopes` holds the affected scope, contained work and transitive
consumers, preserving independent work and prerequisites. Unknown impact must be resolved or
conservatively represented as all affected scope; it cannot silently release work. Re-read current
judgments at admission and final closure. A hold is never acceptance or permission to weaken criteria.

`PlanningFinding.StatusSummary` presents what needs reconciliation, scoped impact, actor, next action
and whether a user decision is required. Instructions to escalate *if* evidence fails are not evidence
of failure. Commission ordinary missing responsibilities using `WorkContributions` and track delivery
with `TrackedCollaboration`; advice is reviewed against the exact approved inputs before closing a
finding. Actual incompatible requirements go to the authorized manager with supporting evidence.
Only the existing authority policy may reserve a decision for the user. Replays reuse source-bound
finding/session keys, and changed or superseded sources must not retain stale alerts.


## Durable issues and restart-safe state (5.31.0)

`PlatformCapabilityClient.Issues` provides authorized discovery, reconciliation and durable commission intent through `work.issues.read.v1`, `work.issues.reconcile.v1` and `work.issues.commission.v1`. Initial registry scope is an authorized project board with an exact accepted source. It supports game and non-game project work without a fixed team, stage sequence, consultation count or semantic similarity threshold. A model judges obligation equivalence with `PlanningObligationCatalog`; code binds retained identities, reference selections and revisions.

Read current cases before acting. Keep distinct obligations separate, consolidate compatible consultations by judgment, and retain every required decision. Advice received moves handling to review; only grounded owner reconciliation with explicit evidence resolves the case. A changed accepted source or a new authorized `ReopeningEvidence` link can start another episode. Rephrasing alone cannot reopen a resolved obligation. Cases never grant execution, hiring authority or product acceptance.

`DurableAgentState.UpdateAsync` merges with current typed state and derives mutation identity from intended content and observed revision. `UpdateVersionedAsync` also exposes that revision when intentional clears must be distinguished from stale snapshots. Use domain merges for independent child records. Do not derive mutation keys only from clocks or planning fingerprints. After an interrupted accepted response, reread and merge; exact mutation replay returns its original receipt without rolling back newer state. Reusing a key for different content is a typed conflict.

A commission is recorded before dispatch. The platform commits session, tracking, case links and wake outbox atomically, and rediscovery resumes after restart. `WorkIssueEvents.Changed` wakes the owning manager through the manager base; events convey no authority. `SupersededSessionIds` carries explicit legacy relationships. Current accepted sources and actor ownership are checked again before idle legacy tracking is retired. Leased work and historical failures are retained. `RetireTrackingAsync` accepts a terminal issue receipt and archives only the specified owner's idle typed collaboration cards.

Subscribe to `work.issues.changed.v1`, request the three issue capabilities through the normal reviewed manifest, and retain scoped board and artifact authorization. Missing authority must be surfaced for the responsible manager; a notification or manifest does not grant permission. Preserve full evidence and use concise judgment-authored briefs for Overview.
## Current planning facts and advice receipts (5.32.0)

`PlanningResourceContext.ReadAsync` reads the exact project team's roster, project/team staffing
requests and project resource requests through existing authorized clients. `Fingerprint` covers
substantive identity, revision, approval and readiness facts, not changing report timestamps or
wording. A pending request, approved plan, filled seat and Ready resource are distinct facts. This
snapshot does not grant execution authority or impose a required team composition. A missing
execution resource need not block decomposition or estimates.

The requesting agent routes `WorkContributions.IsReply` to `ReceiveAsync` before domain-specific
intake. `ReadAnswerAsync` also supports recovery of a complete specialist answer followed by an
older requester's incorrect finalization. It revalidates speaker, key, final page, board ownership
and exact accepted sources. Callers exclude cancelled/failed sessions and bind retained scope;
neither a session summary nor completed advice proves execution or closes an obligation.

`DecisionContinuations` follows explicit replacement references, rejecting cycles, missing links
and cross-project references. A cached terminal status is not the current direction after replacement.
Agents can use `DecisionRecord` requester/decider identities (Contracts 5.9.0) to distinguish their
own judgments from human decisions. Missing authorship from an older host remains unknown.
