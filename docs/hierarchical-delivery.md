# Hierarchical execution in SDK 3.59.0

Declare `work.execution.run.v2` alongside preserved V1 support. `CSweetAgentBase` adapts Task scope to the existing task callback; override `ExecuteDeliveryScopeAsync` for Story/Epic/Release. Aggregate assignments never fabricate sprint identifiers. Validate the authoritative active plan, revision, principal and candidate before returning a result.

Use typed `PlatformWorkClient` delivery read/configure/control/evidence/review/accept/recover operations. Scope membership grants no board/artifact/repository access. Managers activate plans and accept exact candidates; architects configure topology within grants. Merges remain trusted platform operations.

`HierarchicalProjectDelivery.PrepareAsync` pins scope and independent staffing. Planning `DeliveryKind = "Artifact"` permits document-only delivery without repositories. Every task requires an independent quality review: QA reviews product deliverables, and the board manager reviews QA-owned evidence artifacts. `ArtifactTaskDelivery` publishes exact IDs/revisions/hashes; `ArtifactDeliveryReview` checks identity and criteria. `DeliveryTaskTechnicalReview` binds source and story target commits before integration. Rework uses a new reviewed branch from current story state.

`DeliveryCandidateWorkspace` materializes digest-verified authorized snapshots in owned temporary directories, rejects unsafe archives and detects source mutation. `DeliveryScopeReview` supplies technical reviewers with bounded source, exact documents, child outcomes and builds. QA executes actual tests for every repository and returns commit-bound command results. Manager acceptance consumes QA/technical evidence and grants no deployment authority.

Build IDs bind certified provider/recipe/target/configuration and source revision. `WorkDeliveryBuildPending` returns durable pending identities; platform reconciliation resumes readiness without agent polling loops. `WorkDeliveryFindingResolution` links QA-verified remediation to rejected findings. Domain keys and per-repository receipts protect retries and partial recovery.

SDK 3.64.1 supplies `DeliveryBuildReadiness.ExecuteAsync` for Release `build-readiness`.
`HierarchicalProjectDelivery.SelectBuildReadinessRole` prefers an eligible Build Release Engineer,
then a Technical Director or Developer (including Game Engineer) with V2 execution and explicit
`work.execution.build-readiness.v1` support. Legacy developers without the handler are excluded.
The selected role and capabilities are pinned as assignment requirements. The handler re-reads
the active plan, stage principal and exact candidate before requesting each configured certified
build. Source authors may request these mechanical builds; independent product QA and technical
review still exclude authors under their existing rules. Pending identities remain event-driven.
No host build tools, candidate promotion, deployment or public release permission is added.

Run SDK tests and package/template verification. Synchronize downstream manifests, package pins, self-tests and version-matched release notes. V1 contracts/historical profiles remain supported.

## QA evidence ownership (3.62.0)

QA may author its own Artifact test plan/report and remain the product's aggregate QA reviewer. The board manager independently reviews that exact evidence artifact against all criteria; task-level self-review is still rejected. QA cannot review any implementation or non-QA artifact it authored. Aggregate technical review excludes code authors rather than authors of architecture documents. Missing reviewers raise DeliveryStaffingException so managers can surface the task/stage/role gap without crashing.

## Compute test readiness (3.64.1)

DeliveryComputeTest supports compute-test release bindings without certified packaging. It discovers application-managed project compute options and selects a compatible Docker or static recipe from the immutable source archive. All commands execute inside owned guest compute through existing grants. Source hash, exact command, exit status, HTTP marker, environment generation and provider URL receipts are validated by the platform. Pending operations and authority/setup waits retain durable state; wake hints and five-minute bounded discovery resume progress. Unknown results never authorize replacement command keys. Independent QA and technical review remain required. No host shell, provider credential, automatic network grant or public deployment is added.

Draft QA staffing: HierarchicalProjectDelivery.PrepareAsync includes ConfigureWorkDeliveryPlanRequest.TaskAssignments before creating the draft, then finalizes task delivery and activates. The host checks actual assignments before dispatch; proposed QA-report manager reviews never grant execution. Delivery planning conflicts retain the platform task-specific reason as a recoverable DeliveryStaffingException.
