# Authoring agents under the operating contract

Protocol-v2 agent manifests may declare a role policy:

```json
"rolePolicy": {
  "profile": "manager.v1",
  "declaredRoleKeys": ["software-product-manager"]
}
```

Supported profiles are `manager.v1`, `individual-contributor.v1`, `independent-reviewer.v1`, and `executive-advisor.v1`.

Use `requires[].modelVisible: false` when agent code needs a granted capability but the configured model must not receive that tool. Model tools are derived from approved manifest requirements and effective provider bindings; code should not load a broad tool set and filter it by function name.

Continuous agents should send startup, recovery, periodic, and `StateChanged` attention through one reconciler. Read authoritative systems on every cycle, then store the resulting previous assessment with `ReadOperatingStateAsync` and `WriteOperatingStateAsync`. Writes use an expected revision and idempotency key; on conflict, reread and reassess.

Memory is supporting narrative context only. Assignments, approvals, staffing viability, workflow state, grants, and replay safety remain platform-owned.

## Software Architect reference pattern

The Software Architect is an `individual-contributor.v1` technical authority, not a people
manager. A reusable planning flow separates recommendation from commitment:

1. The PM publishes `product-management.architecture-brief.v2` with approved outcome Epics,
   constraints, acceptance criteria, and authoritative source revisions.
2. The Architect publishes `software-architecture.design-proposal.v1`. C-Sweet computes and
   persists the artifact digest.
3. The PM returns `product-management.architecture-decision.v1` for that exact digest. It may
   approve, reject, or request one bounded revision; three revisions require a focused manager
   decision.
4. Only an approved digest can anchor `software-architecture.story-proposal.v2` and paged
   `software-architecture.task-proposal.v2` artifacts.
5. Task proposals carry role and capability recommendations, safe parallelization groups, and
   rationale. They never carry employee or installation IDs. The PM filters its approved pool and
   selects exact assignees by stable lowest-load ordering.

The interaction is manager-led: **directive → deliverable or clarification → decision plus next
directive**. Every nonterminal turn carries a recognized typed artifact. If product information is
missing, the Architect returns one `software-architecture.question.v2` batch. The PM answers within
its mandate and reissues the current brief linked to the question digest. An architecture decision
embeds the next brief, so approval can lead directly into Story planning without an acknowledgement
or follow-up chat turn. Human-readable content is only a UI summary.

Use `AgentCoordinationTranscript` to find the latest artifact by type and speaker, deserialize its
payload, and verify a digest before continuing. On restart or a text-only turn, recover the next
stage from persisted artifacts; never ask another agent to resend context that is already in the
coordination transcript.

Persist the approved digest in each Story and Task `WorkItemPlanningSpecification` as
`architectureArtifactDigest`. This is the authoritative board-to-coordination link used by later
support and drift checks; copying the design narrative into memory or ticket prose is insufficient.

The Architect's attention reconciler rereads roster, boards, sprints, reports, orchestration,
coordination, and personal commitments. It persists
`com.csweet.software-architect.assessment` version 1. An unchanged healthy cycle performs no model
call or message. An unchanged degraded cycle preserves its existing commitment rather than
duplicating support or escalation.

## Work-item technical support pattern

A Developer opens `communication.coordination.start-work.v1` only for a genuine technical failure.
The request pins board, item, sprint execution, stage execution, and assignment revision and carries
`software-development.support-request.v1` with sanitized diagnostics. Provider, credential, grant,
repository authorization, and platform availability failures use operational escalation instead.

C-Sweet verifies that the participants are the exact assigned Developer and a same-team Architect.
The session is capped at six turns. The Architect returns
`software-architecture.guidance.v1`; scope, architecture, budget, timing, or risk changes block for
PM approval. C-Sweet links request and completion comments to the work item and guidance digest.
The Developer consumes linked guidance on the next attempt and requests a retry only for the exact
blocked stage and unchanged assignment revision. The platform remains the sole transition owner.

## Failure-mode checklist

- Reject generic planning chat from ordinary employees; allow only the accountable PM or an
  authorized manager/executive.
- Fail closed on missing, duplicate, or non-model-visible exact tool bindings.
- Never use memory as an approval, assignment, blocker, workflow, or idempotency source.
- Never mutate an executing assignment snapshot. Apply recommendations only to future work.
- Do not acknowledge coordination autonomously or continue past the turn bound.
- Do not emit a nonterminal text-only turn; pair it with the typed artifact that owns the next action.
- Specialists recover prior manager directives from the transcript instead of asking managers to
  repair transport metadata.
- Deduplicate attention commitments and support sessions using stable domain correlations.
- Reread all authoritative sources after an operating-state compare-and-swap conflict.
