# Hierarchical execution in SDK 3.59.0

Declare `work.execution.run.v2` alongside preserved V1 support. `CSweetAgentBase` adapts Task scope to the existing task callback; override `ExecuteDeliveryScopeAsync` for Story/Epic/Release. Aggregate assignments never fabricate sprint identifiers. Validate the authoritative active plan, revision, principal and candidate before returning a result.

Use typed `PlatformWorkClient` delivery read/configure/control/evidence/review/accept/recover operations. Scope membership grants no board/artifact/repository access. Managers activate plans and accept exact candidates; architects configure topology within grants. Merges remain trusted platform operations.

`HierarchicalProjectDelivery.PrepareAsync` pins scope and independent staffing. Planning `DeliveryKind = "Artifact"` permits document-only delivery without repositories. Every task requires QA. `ArtifactTaskDelivery` publishes exact IDs/revisions/hashes; `ArtifactDeliveryReview` checks identity and criteria. `DeliveryTaskTechnicalReview` binds source and story target commits before integration. Rework uses a new reviewed branch from current story state.

`DeliveryCandidateWorkspace` materializes digest-verified authorized snapshots in owned temporary directories, rejects unsafe archives and detects source mutation. `DeliveryScopeReview` supplies technical reviewers with bounded source, exact documents, child outcomes and builds. QA executes actual tests for every repository and returns commit-bound command results. Manager acceptance consumes QA/technical evidence and grants no deployment authority.

Build IDs bind certified provider/recipe/target/configuration and source revision. `WorkDeliveryBuildPending` returns durable pending identities; platform reconciliation resumes readiness without agent polling loops. `WorkDeliveryFindingResolution` links QA-verified remediation to rejected findings. Domain keys and per-repository receipts protect retries and partial recovery.

Run SDK tests and package/template verification. Synchronize downstream manifests, package pins, self-tests and version-matched release notes. V1 contracts/historical profiles remain supported.
