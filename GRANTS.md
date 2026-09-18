# Generated capability reference

## Durable connector actions (SDK 3.35.0)

`platform.connector.action.request.v1` creates an exact, durable proposal for a declared connector
operation through `context.Platform.Connectors.RequestActionAsync`. The consuming installation
also needs its independent grant and binding for the requested provider capability. The host derives
the account and approver; no request can supply credentials, an installation identity or a destination.
`platform.connector.action.read.v1` reads only the caller's action through `ReadActionAsync`.
`platform.connector.action.cancel.v1` independently grants `CancelActionAsync` for the caller's
unstarted actions. It cannot undo provider effects or hide an uncertain outcome. Action reads now
include bounded authoritative decision feedback; feedback cannot modify or authorize another plan.
Use a stable domain idempotency key, persist the returned action ID, and subscribe to
`com.csweet.connector.action.changed.v1` for wake hints. Read current authoritative status before
advancing work. AwaitingApproval and Approved are not provider success. Indeterminate requires
reconciliation or a visible block, never resubmission with a new key. These controls are not model-visible tools.

This file is checked against `CSweet.Agent.SDK.CapabilityCatalog` by
`CapabilityReferenceDocumentationTests`; adding a capability without documenting it fails the SDK
test gate. Agent and service code must use typed SDK constants rather than repeat wire names.

`CapabilityCatalog.ByService` provides the same names grouped by owning service or feature, while
`CapabilityCatalog.All` and `CapabilityCatalog.IsKnown(...)` support manifest validation and audit
tests.

The platform runtime registry supplies the tool name, full input/output JSON Schemas, scope
resolver, risk class, timeout and size limits, quota class, approval behavior, and owning service.
Provider capabilities supply those fields in their signed manifest-v2 descriptor. `tools/list`
returns only the intersection of this registry, the approved manifest revision, the live
installation grant, and an active same-organization provider binding. Being listed here never
grants access; even baseline operations such as `ask_user` require an explicit grant.

| Capability class | Scope | Risk | Approval | Schema source | Owner |
|---|---|---|---|---|---|
| Read/query | Server-resolved organization or installation | Read-only | None | Runtime registry | Named platform service |
| Explicit low-risk mutation | Server-resolved resource | Advisory write | Policy-dependent | Runtime registry | Named platform service |
| Proposal/action staging | Server-resolved resource | Sensitive write | Always creates an approval | Runtime registry | Named platform service |
| Provider capability | Bound provider installation in the same organization | Manifest-declared | Manifest/policy-declared | Hashed manifest-v2 descriptor | Provider package |

## Assistant

- `AssistantCapabilities.Converse` â€” `assistant.converse.v1`
- `AssistantCapabilities.SummarizeActivity` â€” `assistant.summarize-activity.v1`
- `AssistantCapabilities.PlanWork` â€” `assistant.plan-work.v1`

## Agent lifecycle and configuration

- `AgentConfigurationCapabilities.Describe` â€” `agent.configuration.describe.v1`
- `AgentConfigurationCapabilities.Update` â€” `agent.configuration.update.v1`
- `AgentLifecycleCapabilities.CompleteOnboarding` â€” `agent.onboarding.complete.v1`

## Agent catalog

- `AgentCatalogCapabilities.Search` â€” `platform.agent-catalog.search.v1`

This read-only grant allows an agent to search installed, local-directory, first-party, and
marketplace agent listings through the SDK-managed platform tool. It does not authorize preview, import,
installation, grant changes, hiring, assignment, or spending.

## Platform

- `PlatformCapabilities.LlmChatStream` â€” `platform.llm.chat-stream.v1`
- `PlatformCapabilities.BusinessProfileRead` â€” `platform.business-profile.read.v1`
- `PlatformCapabilities.BusinessProfileUpdateExplicit` â€” `platform.business-profile.update-explicit.v1`
- `PlatformCapabilities.BusinessProfileProposeUpdate` â€” `platform.business-profile.propose-update.v1`
- `PlatformCapabilities.OrganizationSnapshotRead` â€” `platform.organization.snapshot.read.v1`
- `PlatformCapabilities.BusinessPatternSearch` â€” `platform.business-pattern.search.v1`
- `PlatformCapabilities.WorkstreamPlanPropose` â€” `platform.workstream.plan.propose.v1`
- `PlatformCapabilities.WorkstreamRead` â€” `platform.workstream.read.v1`
- `PlatformCapabilities.WorkstreamPlanProposeV2` â€” `platform.workstream.plan.propose.v2`
- `PlatformCapabilities.WorkstreamChangePropose` â€” `platform.workstream.change.propose.v1`
- `PlatformCapabilities.WorkstreamGateRead` â€” `platform.workstream.gate.read.v1`
- `PlatformCapabilities.WorkstreamGateSubmit` â€” `platform.workstream.gate.submit.v1`
- `PlatformCapabilities.WorkstreamGateDecide` â€” `platform.workstream.gate.decide.v1`
- `PlatformCapabilities.PortfolioRead` â€” `platform.management.portfolio.read.v1`
- `PlatformCapabilities.TeamRosterReadV2` â€” `platform.team-roster.read.v2`
- `PlatformCapabilities.DecisionRequest` â€” `platform.decision.request.v1`
- `PlatformCapabilities.DecisionRead` â€” `platform.decision.read.v1`
- `PlatformCapabilities.DecisionDecide` â€” `platform.decision.decide.v1`
- `PlatformCapabilities.WorkforceSearch` â€” `platform.workforce.search.v1`
- `PlatformCapabilities.WorkforcePlanPropose` â€” `platform.workforce-plan.propose.v1`
- `PlatformCapabilities.FinanceProfileRead` â€” `platform.finance-profile.read.v1`
- `PlatformCapabilities.FinanceProfileProposeUpdate` â€” `platform.finance-profile.propose-update.v1`
- `PlatformCapabilities.BudgetEvaluate` â€” `platform.budget.evaluate.v1`
- `PlatformCapabilities.ApprovalPropose` â€” `platform.approval.propose.v1`
- `PlatformCapabilities.ManagementCycleRead` â€” `platform.management-cycle.read.v1`
- `PlatformCapabilities.UserInputRequest` â€” `platform.user-input.request.v1`
- `PlatformCapabilities.HiringRecommendationList` â€” `platform.hiring-recommendation.list.v1`
- `PlatformCapabilities.HiringRecommendationUpsert` â€” `platform.hiring-recommendation.upsert.v1`
- `PlatformCapabilities.HiringRecommendationResolve` â€” `platform.hiring-recommendation.resolve.v1`
- `PlatformCapabilities.HiringRecommendationWithdraw` â€” `platform.hiring-recommendation.withdraw.v1`
- `PlatformCapabilities.ResourceChangePropose` â€” `platform.management.resource-change.propose.v1`
- `PlatformCapabilities.ResourceChangeRead` â€” `platform.management.resource-change.read.v1`
- `PlatformCapabilities.ResourceChangeDecide` â€” `platform.management.resource-change.decide.v1`
- `PlatformCapabilities.HiringWorkflowStage` â€” `platform.hiring-workflow.stage.v1`
- `PlatformCapabilities.UserActionSuggest` â€” `platform.user-action.suggest.v1`

### Infrastructure delivery

- `PlatformCapabilities.InfrastructureEnvironmentRead` â€” `platform.infrastructure.environment.read.v1`
- `PlatformCapabilities.InfrastructureStateWrite` â€” `platform.infrastructure.state.write.v1`
- `PlatformCapabilities.InfrastructureChangePropose` â€” `platform.infrastructure.change.propose.v1`
- `PlatformCapabilities.InfrastructureChangeRead` â€” `platform.infrastructure.change.read.v1`
- `PlatformCapabilities.InfrastructureOperationExecute` â€” `platform.infrastructure.operation.execute.v1`
- `PlatformCapabilities.InfrastructureReconcile` â€” `platform.infrastructure.reconcile.v1`
- `PlatformCapabilities.InfrastructureDeploymentContractPublish` â€” `platform.infrastructure.deployment-contract.publish.v1`
- `PlatformCapabilities.InfrastructureFileTransfer` â€” `platform.infrastructure.file-transfer.v1`
- `InfrastructureCapabilityNames.EnvironmentRead` â€” `platform.infrastructure.environment.read.v1`
- `InfrastructureCapabilityNames.StateWrite` â€” `platform.infrastructure.state.write.v1`
- `InfrastructureCapabilityNames.ChangePropose` â€” `platform.infrastructure.change.propose.v1`
- `InfrastructureCapabilityNames.ChangeRead` â€” `platform.infrastructure.change.read.v1`
- `InfrastructureCapabilityNames.OperationExecute` â€” `platform.infrastructure.operation.execute.v1`
- `InfrastructureCapabilityNames.Reconcile` â€” `platform.infrastructure.reconcile.v1`
- `InfrastructureCapabilityNames.DeploymentContractPublish` â€” `platform.infrastructure.deployment-contract.publish.v1`
- `InfrastructureCapabilityNames.FileTransfer` â€” `platform.infrastructure.file-transfer.v1`

- `PlatformCapabilities.TeamRosterRead` â€” `platform.team-roster.read.v1`

`TeamRosterRead` is resolved from the authenticated installation to its active agent employee and
sole eligible team. It exposes bounded employee IDs, display names, human/agent type, company and
team roles, relationship, presence, and complete role-coverage counts. It never exposes email,
application-user IDs, installation or package identity, permissions, credentials, costs, prompts,
memory, or unrelated employees. Team membership does not grant any other authority.

### Documents and design packages

- `PlatformCapabilities.ArtifactDecideV2` â€” `platform.artifact.decide.v2`

### Generic delivery evidence

- `PlatformCapabilities.ToolchainCatalogRead` â€” `platform.toolchain.catalog.read.v2`
- `PlatformCapabilities.BuildRequest` â€” `platform.build.request.v2`
- `PlatformCapabilities.BuildRead` â€” `platform.build.read.v2`
- `PlatformCapabilities.BuildClaim` â€” `platform.build.claim.v1`
- `PlatformCapabilities.BuildHeartbeat` â€” `platform.build.heartbeat.v1`
- `PlatformCapabilities.BuildReport` â€” `platform.build.report.v2`
- `PlatformCapabilities.BuildCancel` â€” `platform.build.cancel.v1`
- `PlatformCapabilities.ValidationRead` â€” `platform.validation.read.v2`
- `PlatformCapabilities.PreviewCreate` â€” `platform.preview.create.v2`
- `PlatformCapabilities.PreviewRead` â€” `platform.preview.read.v2`
- `PlatformCapabilities.EvaluationPlan` â€” `platform.evaluation-session.plan.v1`
- `PlatformCapabilities.EvaluationRead` â€” `platform.evaluation-session.read.v1`
- `PlatformCapabilities.EvaluationReport` â€” `platform.evaluation-session.report.v1`
- `PlatformCapabilities.ReleaseReadinessRead` â€” `platform.release-readiness.read.v1`
- `PlatformCapabilities.ReleaseReadinessSubmit` â€” `platform.release-readiness.submit.v1`
- `PlatformCapabilities.PublicationPropose` â€” `platform.publication.propose.v1`

These grants operate on generic Workstream-scoped resources. Profile and type keys carry domain
meaning; capability names must not be specialized for individual industries.

- `PlatformCapabilities.ArtifactCreate` â€” `platform.artifact.create.v1`
- `PlatformCapabilities.ArtifactRead` â€” `platform.artifact.read.v1`
- `PlatformCapabilities.ArtifactRevise` â€” `platform.artifact.revise.v1`
- `PlatformCapabilities.ArtifactSubmit` â€” `platform.artifact.submit.v1`
- `PlatformCapabilities.ArtifactDecide` â€” `platform.artifact.decide.v1`
- `PlatformCapabilities.ArtifactRequestAccess` â€” `platform.artifact.request-access.v1`
- `PlatformCapabilities.ArtifactPackageCreate` â€” `platform.artifact-package.create.v1`
- `PlatformCapabilities.ArtifactPackageRead` â€” `platform.artifact-package.read.v1`
- `PlatformCapabilities.ArtifactPackageSubmit` â€” `platform.artifact-package.submit.v1`
- `PlatformCapabilities.ArtifactPackageDecide` â€” `platform.artifact-package.decide.v1`

Artifact tool exposure is only the first authorization check. Except for organization-scoped
creation, every agent operation also requires an active grant for the exact artifact and action;
folders, chats, assignments, packages, and job titles do not confer access. Package operations
check every member independently. Agents use `ArtifactRequestAccess` to create a human-reviewed
request and can resume from `ArtifactEvents.AccessDecision`, whose payload deliberately excludes
document content.

## Memory

- `MemoryCapabilities.BusinessRead` â€” `memory.business.read.v1`
- `MemoryCapabilities.BusinessPropose` â€” `memory.business.propose.v1`
- `MemoryCapabilities.UserRead` â€” `memory.user.read.v1`
- `MemoryCapabilities.UserPropose` â€” `memory.user.propose.v1`

## Communication

- `CommunicationCapabilities.ChatRead` â€” `communication.chat.read.v1`
- `CommunicationCapabilities.ChatCreate` â€” `communication.chat.create.v1`
- `CommunicationCapabilities.ChatModify` â€” `communication.chat.modify.v1`
- `CommunicationCapabilities.ChatDelete` â€” `communication.chat.delete.v1`
- `CommunicationCapabilities.MessageSend` â€” `communication.message.send.v1`
- `CommunicationCapabilities.CoordinationStart` â€” `communication.coordination.start.v1`
- `CommunicationCapabilities.CoordinationStartWork` â€” `communication.coordination.start-work.v1`
- `CommunicationCapabilities.CoordinationStartBoard` â€” `communication.coordination.start-board.v1`
- `CommunicationCapabilities.CoordinationRespond` â€” `communication.coordination.respond.v1`
- `CommunicationCapabilities.CoordinationRead` â€” `communication.coordination.read.v1`
- `CommunicationCapabilities.CoordinationList` â€” `communication.coordination.list.v1`
- `CommunicationCapabilities.CoordinationResume` â€” `communication.coordination.resume.v1`
- `CommunicationCapabilities.CoordinationCancel` â€” `communication.coordination.cancel.v1`
- `CommunicationCapabilities.MessageIngest` â€” `communication.message.ingest.v1`
- `CommunicationCapabilities.Send` â€” `communication.send.v1`
- `CommunicationCapabilities.WorkspaceApply` â€” `communication.workspace.apply.v1`
- `CommunicationCapabilities.IdentityAssign` â€” `communication.identity.assign.v1`
- `CommunicationCapabilities.LinkCodeRegister` â€” `communication.link-code.register.v1`

## Management and product leadership

- `ManagementCapabilities.CheckIn` â€” `management.check-in.v1`
- `ProductManagementCapabilities.RoleBrief` â€” `management.product-role-brief.v1`
- `ProductManagementCapabilities.PlanReview` â€” `management.product-plan.review.v1`
- `ProductManagementCapabilities.Escalation` â€” `management.product-escalation.v1`
- `ProductManagementCapabilities.Plan` â€” `product-management.plan.v1`
- `ProductManagementCapabilities.ContextUpdate` â€” `product-management.context.update.v1`

## Work management

New governed software-board mutations:

- `WorkBoardCapabilities.Configure` â€” `work.board.configure`
- `WorkBoardCapabilities.ConfigureColumns` â€” `work.board.columns.configure`
- `WorkOrchestrationCapabilities.ConfigureSoftwareTemplate` â€” `work.orchestration.software-template.configure`
- `WorkOrchestrationCapabilities.ConfigureProfile` â€” `work.orchestration.profile.configure.v1`
- `WorkFlowMetricCapabilities.Read` â€” `work.flow-metrics.read.v1`

Work-management capabilities require both an approved package capability and a live scoped grant
on the organization or board. SDK calls never bypass the board grant model. Mutation requests
include an idempotency key and, where applicable, the last observed resource revision.
Comment updates and deletions are author-scoped: the platform permits an agent to change only
comments its own installation created, and rejects a stale expected revision rather than
overwriting a concurrent edit. Deletion is soft, so the comment's activity and audit history
remain durable while the body disappears from reads.

- `WorkBoardCapabilities.Read` â€” `work.board.read`
- `WorkBoardCapabilities.Create` â€” `work.board.create`
- `WorkItemCapabilities.Read` â€” `work.item.read`
- `WorkItemCapabilities.Start` â€” `work.item.start`
- `WorkItemCapabilities.Create` â€” `work.item.create`
- `WorkItemCapabilities.ReadTypes` â€” `work.item.types.read.v1`
- `WorkItemCapabilities.RevisePlanning` â€” `work.item.planning.revise.v1`
- `WorkItemCapabilities.DecideApproval` â€” `work.item.approval.decide.v1`
- `WorkItemCapabilities.FinalizeDelivery` â€” `work.item.delivery.finalize`
- `WorkItemCapabilities.Comment` â€” `work.item.comment`
- `WorkItemCapabilities.ReadComments` â€” `work.item.comments.read`
- `WorkItemCapabilities.CommentUpdate` â€” `work.item.comment.update.v1`
- `WorkItemCapabilities.CommentDelete` â€” `work.item.comment.delete.v1`
- `WorkItemCapabilities.Estimate` â€” `work.item.estimate`
- `WorkItemCapabilities.Move` â€” `work.item.move`
- `WorkItemCapabilities.Complete` â€” `work.item.complete`
- `WorkItemCapabilities.Cancel` â€” `work.item.cancel`
- `WorkItemCapabilities.Reopen` â€” `work.item.reopen`
- `WorkItemCapabilities.Transfer` â€” `work.item.transfer`
- `WorkItemCapabilities.QualitySubmit` â€” `work.item.quality.submit`
- `WorkSprintCapabilities.Read` â€” `work.sprint.read`
- `WorkSprintCapabilities.Create` â€” `work.sprint.create`
- `WorkSprintCapabilities.Start` â€” `work.sprint.start`
- `WorkSprintCapabilities.Complete` â€” `work.sprint.complete`
- `WorkSprintCapabilities.Cancel` â€” `work.sprint.cancel`
- `WorkSprintCapabilities.ManageScope` â€” `work.sprint.scope.manage`
- `WorkSprintCapabilities.ManageCapacity` â€” `work.sprint.capacity.manage`
- `WorkSprintCapabilities.CarryOver` â€” `work.sprint.carryover`
- `WorkSprintCapabilities.ReadReports` â€” `work.sprint.report.read`
- `WorkAutomationCapabilities.Read` â€” `work.automation.read`
- `WorkAutomationCapabilities.Manage` â€” `work.automation.manage`
- `PersonalTodoCapabilities.Read` â€” `work.personal-todo.read.v1`
- `PersonalTodoCapabilities.Add` â€” `work.personal-todo.add.v1`
- `PersonalTodoCapabilities.Reorder` â€” `work.personal-todo.reorder.v1`
- `PersonalTodoCapabilities.Requeue` â€” `work.personal-todo.requeue.v1`
- `PersonalTodoCapabilities.Activate` â€” `work.personal-todo.activate.v1`
- `PersonalTodoCapabilities.Claim` â€” `work.personal-todo.claim.v1` (SDK runtime only)
- `PersonalTodoCapabilities.Complete` â€” `work.personal-todo.complete.v1` (SDK runtime only)
- `PersonalTodoCapabilities.Block` â€” `work.personal-todo.block.v1` (SDK runtime only)
- `PersonalTodoCapabilities.Release` â€” `work.personal-todo.release.v1` (SDK runtime only)
- `PersonalTodoCapabilities.Defer` â€” `work.personal-todo.defer.v1` (SDK runtime only)
- `PersonalTodoCapabilities.Update` â€” `work.personal-todo.update.v1`
- `PersonalTodoCapabilities.Archive` â€” `work.personal-todo.archive.v1`
- `PersonalTodoCapabilities.Restore` â€” `work.personal-todo.restore.v1`
- `CapabilityNames.WorkManagement.PersonalTodoCancel` â€” `work.personal-todo.cancel.v1`

### Board orchestration

- `WorkOrchestrationCapabilities.Read` â€” `work.orchestration.read`
- `WorkOrchestrationCapabilities.Preflight` â€” `work.orchestration.preflight`
- `WorkOrchestrationCapabilities.Start` â€” `work.orchestration.start`
- `WorkOrchestrationCapabilities.Pause` â€” `work.orchestration.pause`
- `WorkOrchestrationCapabilities.Resume` â€” `work.orchestration.resume`
- `WorkOrchestrationCapabilities.Cancel` â€” `work.orchestration.cancel`
- `WorkOrchestrationCapabilities.Retry` â€” `work.orchestration.retry`
- `WorkOrchestrationCapabilities.Execute` â€” `work.execution.run.v1`

## Source control and Git workspace

- `SourceControlCapabilities.TeamRepositoryOptions` â€” `source-control.repository.team-options.v2`
- `SourceControlCapabilities.ProvisionRepository` â€” `source-control.repository.provision.v2`

Git workspace operations are scoped to an assigned work item and its authoritative assignment
revision. Core derives the repository, base commit, and deterministic ticket branch. Agent
containers receive a credential-free tree without `.git`; provider credentials and installation
identifiers never appear in these contracts.

- `GitWorkspaceCapabilities.Prepare` â€” `git.workspace.prepare.v2`
- `GitWorkspaceCapabilities.Refresh` â€” `git.workspace.refresh.v2`
- `GitWorkspaceCapabilities.Inspect` â€” `git.workspace.inspect.v2`
- `GitWorkspaceCapabilities.Publish` â€” `git.workspace.publish.v2`
- `GitWorkspaceCapabilities.Cleanup` â€” `git.workspace.cleanup.v2`
- `GitMergeCapabilities.Review` â€” `git.merge.review.v2`
- `GitMergeCapabilities.Authorize` â€” `git.merge.authorize.v2`

## Web proxy

- `WebCapabilities.Fetch` â€” `web.fetch.v1`
- `WebCapabilities.Request` â€” `web.request.v1`
- `WebCapabilities.Render` â€” `web.render.v1`
- `WebCapabilities.Socket` â€” `web.socket.v1`

## Plugin runtime

- `PluginCapabilities.State` â€” `plugin.state.v1`

## Secure plugin operations

These provider-neutral broker capabilities keep credentials, provider upload sessions, and
operational storage outside plugin processes. Installations receive only the individual grants
declared and approved in their manifest.

- `CapabilityNames.ManagedActionExecute` â€” `platform.managed-action.execute.v1`
- `PlatformCapabilities.ManagedActionDecide` â€” `platform.managed-action.decide.v1` (restricted to the exact assigned agent-approver installation after a durable `com.csweet.managed-action.approval-requested.v1` event)
- `CapabilityNames.EngagementInboxUpsert` â€” `platform.engagement-inbox.upsert.v1`
- `CapabilityNames.MetricSnapshotWrite` â€” `platform.metric-snapshot.write.v1`
- `CapabilityNames.SynchronizationCheckpoint` â€” `platform.synchronization-checkpoint.v1`
- `PlatformCapabilities.AgentOperatingStateRead` â€” `platform.agent-operating-state.read.v1`
- `PlatformCapabilities.AgentOperatingStateWrite` â€” `platform.agent-operating-state.write.v1`
- `PlatformCapabilities.StaffingReplenishmentPropose` â€” `platform.management.staffing-replenishment.propose.v1`
- `PlatformCapabilities.StaffingReplenishmentRead` â€” `platform.management.staffing-replenishment.read.v1`
- `PlatformCapabilities.StaffingReplenishmentDecide` â€” `platform.management.staffing-replenishment.decide.v1`
- `CapabilityNames.MediaTransfer` â€” `platform.media.transfer.v1`

## Contribution rule

Every new platform capability must be added to the runtime registry and `CapabilityNames`, exposed
through an appropriate typed helper, included in `CapabilityCatalog.ByService`, documented here,
and covered by authorization, schema, quota, approval, and audit tests. Provider descriptors must
be valid, hashed manifest-v2 declarations.


SDK 3.35.0 adds assignment-scoped internal Git LFS locks through `context.Platform.Git.ListLocksAsync`, `LockFileAsync`, and `UnlockFileAsync`. Declare `git.workspace.locks.read.v2`, `git.workspace.locks.create.v2`, and `git.workspace.locks.release.v2` as needed (the separate `git-file-locks` capability group does not expand existing workspace grants). Core derives repository and employee ownership from the current assignment and team grant. Agents cannot choose owner identities, force another owner's unlock, or access provider credentials. Repeat acquisition of the same owned path returns the existing lock; repeat release is harmless. Own locks permit work-branch publication; release them before a governed merge. Managers can release orphaned locks. GitHub agent-owned locks are not supported by this API.

## Coordination document sharing (SDK 3.35.0)

Typed collaboration actions use existing coordination authority. At chat, board, and work-item
starts and participant replies, Core verifies creator/steward ownership, current document read
authority, organization, and exact revision/hash before granting the other authenticated
participant document-level read. This includes other revisions; it is not revision-only access.
No revise/decide/submit permission is granted by sharing. All references are validated before
grant mutation; session persistence and grants commit together. Review/handoff declarations
are not formal artifact approval. Runtime scheduling for dependency waits uses personal-to-do
deferral; no new event subscription mechanism is introduced.

`work.orchestration.approval.decide` (`WorkOrchestrationCapabilities.DecideApproval`) allows the assigned board manager to submit a reviewed approval or rejection through `Work.DecideApprovalStageAsync`. Requires a scoped board grant and stable decision idempotency key; this does not approve hiring, spending, or repository merges.

## Calendar

- `work.calendar.read.v1` â€” business scope; event ownership and reporting authority are checked on every operation.
- `work.calendar.create.v1` â€” business scope; event ownership and reporting authority are checked on every operation.
- `work.calendar.update.v1` â€” business scope; event ownership and reporting authority are checked on every operation.
- `work.calendar.cancel.v1` â€” business scope; event ownership and reporting authority are checked on every operation.
- `work.calendar.schedule.v1` â€” business scope; event ownership and reporting authority are checked on every operation.
## Generic compute

The typed compute client uses compute.provision/read/list/start/stop/restart/destroy/execute.v1 and network.publish-port.v1. Port publication additionally requires network.inbound.v1. Each action has independent current scoped constraints; provisioning alone grants neither inbound access nor port publishing. OS, template, resource, lifetime and persistence limits stay broker-owned. See [compute](docs/compute.md).


| Capability | Purpose |
| --- | --- |
| `compute.provision.v1` | Request an environment under template, resource, lifetime and persistence constraints. |
| `compute.read.v1` | Read the caller's environment or operation result. |
| `compute.list.v1` | Bounded discovery within an authorized workstream. |
| `compute.start.v1` | Start an owned environment at the expected generation. |
| `compute.stop.v1` | Stop an owned environment at the expected generation. |
| `compute.restart.v1` | Restart an owned environment at the expected generation. |
| `compute.destroy.v1` | Request destruction and await confirmed teardown. |
| `compute.execute.v1` | Queue a bounded guest command. |
| `network.inbound.v1` | Independently authorize inbound guest access; not a direct SDK mutation. |
| `network.publish-port.v1` | Publish an explicitly permitted guest port with a bounded lifetime. |
`source-control.personal-work.reserve.v1` permits an opted-in agent to reserve one deterministic private repository for its own Ready personal ticket without claiming the ticket or preparing a workspace. The request is revision-fenced and cannot select credentials, providers, refs, or another owner.

`source-control.personal-work.prepare.v1` permits preparation of an owned, actively claimed personal ticket in a private C-Sweet repository. It is a separate installation approval; it does not permit choosing repositories, credentials, refs, merging, or networking.

## Isolated source snapshots (SDK 3.46.1)

`git.workspace.sync.v1` transfers an authorized workspace snapshot through Core. `context.Platform.Git.MaterializeAsync` creates a runtime-local writable copy and returns its local path; the platform path from Prepare is an opaque workspace location, not a shared filesystem mount. `UploadAsync` sends edited files back before inspection/publication. Existing edits are preserved on repeated materialization; after runtime loss, the latest uploaded snapshot is restored.

Transfer requires the sync declaration plus existing preparation/publication authority for the exact assignment. No repository coordinates or credentials are accepted. Limits are 512 KiB compressed per snapshot, 16 MiB content and 4,096 files. Git metadata, redirected paths and traversal are rejected; local `.csweet` control files are excluded from uploads. An uploaded snapshot does not itself publish a commit.

## Task delivery and merge preferences (3.50.0)

- `TaskDeliveryCapabilities.Submit` — `source-control.task-review.submit.v1`
- `TaskDeliveryCapabilities.List` — `source-control.task-review.list.v1`
- `TaskDeliveryCapabilities.Read` — `source-control.task-review.read.v1`
- `TaskDeliveryCapabilities.Decide` — `source-control.task-review.decide.v1`
- `TaskDeliveryCapabilities.Quality` — `source-control.task-review.quality.v1`
- `TaskDeliveryCapabilities.Preferences` — `source-control.merge-preference.read.v1`
- `TaskDeliveryCapabilities.ChangePreference` — `source-control.merge-preference.change.v1`

These grants permit only current assigned review work and manager-authorized decisions. They do not grant an agent direct merge authority. Scope preferences require the current manager’s retained instruction and expected revision. QA snapshot sync does not permit publication.
