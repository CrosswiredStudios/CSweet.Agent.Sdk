# Canonical execution in SDK 4.1.0

SDK 4.1.0 uses `WorkArtifactDelivery` from WorkManagement.Contracts 4.0.0 for
both author output and artifact QA. Authors call `new WorkArtifactDelivery(artifactId,
revisionId, sha256).ToOutput()` rather than constructing anonymous JSON. The reader
accepts existing persisted PascalCase outcomes without rewriting history, and rejects
malformed or ambiguous identities. QA still verifies the exact document digest and
every acceptance criterion. A malformed later author result cannot fall back to an
older valid delivery. `ArtifactTaskDeliveryHandoffTests` covers document creation,
submission, persistence and independent review; the Technical Director's
`ArtifactDeliveryHandoffTests` exercises its actual completed-state callback and
legacy outcomes through the SDK's production review path.

Declare only `work.execution.run`, with the scope-aware `WorkExecutionAssignment` schema. `CSweetAgentBase` passes Task scope unchanged to `ExecuteCapabilityCoreAsync`; override `ExecuteDeliveryScopeAsync` for Story/Epic/Release. There is no alternate execution capability or task-payload adapter. Task handlers use `RequireTaskScope` and the checked `TaskItemId`/`TaskSprintId` accessors. Aggregate assignments never fabricate sprint identifiers. Validate the authoritative active plan, revision, principal and candidate before returning a result.

Use typed `PlatformWorkClient` delivery read/configure/control/evidence/review/accept/recover operations. Scope membership grants no board/artifact/repository access. Managers activate plans and accept exact candidates; architects configure topology within grants. Merges remain trusted platform operations.

`HierarchicalProjectDelivery.PrepareAsync` pins scope and independent staffing. Planning `DeliveryKind = "Artifact"` permits document-only delivery without repositories. Every task requires an independent quality review: QA reviews product deliverables, and the board manager reviews QA-owned evidence artifacts. `ArtifactTaskDelivery` publishes exact IDs/revisions/hashes; `ArtifactDeliveryReview` checks identity and criteria. `DeliveryTaskTechnicalReview` binds source and story target commits before integration. Rework uses a new reviewed branch from current story state.

`DeliveryCandidateWorkspace` materializes digest-verified authorized snapshots in owned temporary directories, rejects unsafe archives and detects source mutation. `DeliveryScopeReview` supplies technical reviewers with bounded source, exact documents, child outcomes and builds. QA executes actual tests for every repository and returns commit-bound command results. Manager acceptance consumes QA/technical evidence and grants no deployment authority.

Build IDs bind certified provider/recipe/target/configuration and source revision. `WorkDeliveryBuildPending` returns durable pending identities; platform reconciliation resumes readiness without agent polling loops. `WorkDeliveryFindingResolution` links QA-verified remediation to rejected findings. Domain keys and per-repository receipts protect retries and partial recovery.

SDK 4.1.0 supplies `DeliveryBuildReadiness.ExecuteAsync` for Release `build-readiness`.
`HierarchicalProjectDelivery.SelectBuildReadinessRole` prefers an eligible Build Release Engineer,
then a Technical Director or Developer (including Game Engineer) with canonical execution and explicit
`work.execution.build-readiness.v1` support. Legacy developers without the handler are excluded.
The selected role and capabilities are pinned as assignment requirements. The handler re-reads
the active plan, stage principal and exact candidate before requesting each configured certified
build. Source authors may request these mechanical builds; independent product QA and technical
review still exclude authors under their existing rules. Pending identities remain event-driven.
No host build tools, candidate promotion, deployment or public release permission is added.

Run SDK tests and package/template verification. Synchronize downstream manifests, package pins, self-tests and version-matched release notes. This is a coordinated breaking development release; upgrade the platform and executing agents together. Old task envelopes are not translated. Preserve completed evidence and regenerate unfinished assignments through normal authorized recovery after upgrade.

## QA evidence ownership (3.62.0)

QA may author its own Artifact test plan/report and remain the product's aggregate QA reviewer. The board manager independently reviews that exact evidence artifact against all criteria; task-level self-review is still rejected. QA cannot review any implementation or non-QA artifact it authored. Aggregate technical review excludes code authors rather than authors of architecture documents. Missing reviewers raise DeliveryStaffingException so managers can surface the task/stage/role gap without crashing.

## Compute test readiness (3.64.1)

DeliveryComputeTest supports compute-test release bindings without certified packaging. It discovers application-managed project compute options and selects a compatible Docker or static recipe from the immutable source archive. All commands execute inside owned guest compute through existing grants. Source hash, exact command, exit status, HTTP marker, environment generation and provider URL receipts are validated by the platform. Pending operations and authority/setup waits retain durable state; wake hints and five-minute bounded discovery resume progress. Unknown results never authorize replacement command keys. Independent QA and technical review remain required. No host shell, provider credential, automatic network grant or public deployment is added.

Draft QA staffing: HierarchicalProjectDelivery.PrepareAsync includes ConfigureWorkDeliveryPlanRequest.TaskAssignments before creating the draft, then finalizes task delivery and activates. The host checks actual assignments before dispatch; proposed QA-report manager reviews never grant execution. Delivery planning conflicts retain the platform task-specific reason as a recoverable DeliveryStaffingException.

## Draft reconciliation (SDK 4.1.0)

`HierarchicalProjectDelivery.PrepareAsync` rereads current accepted hierarchy before activating an existing Draft. It calls `ConfigureWorkDeliveryPlanRequest` with `PlanId` and `ExpectedRevision` when child membership, planning revision pins or review assignments change. Existing branch and build bindings and release criteria remain explicit. Each task compares accepted delivery content and staffing before its own revision-checked finalization; retries resume from durable state and do not create another release. Selection timestamps alone do not replace assignments. Required specializations and capabilities remain attached to the author selection. Active/paused delivery still requires explicit platform recovery.
