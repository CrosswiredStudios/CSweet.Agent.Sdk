# Generated capability reference

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

- `AssistantCapabilities.Converse` — `assistant.converse.v1`
- `AssistantCapabilities.SummarizeActivity` — `assistant.summarize-activity.v1`
- `AssistantCapabilities.PlanWork` — `assistant.plan-work.v1`

## Agent lifecycle and configuration

- `AgentConfigurationCapabilities.Describe` — `agent.configuration.describe.v1`
- `AgentConfigurationCapabilities.Update` — `agent.configuration.update.v1`
- `AgentLifecycleCapabilities.CompleteOnboarding` — `agent.onboarding.complete.v1`

## Agent catalog

- `AgentCatalogCapabilities.Search` — `platform.agent-catalog.search.v1`

This read-only grant allows an agent to search installed, local-directory, first-party, and
marketplace agent listings through the SDK-managed platform tool. It does not authorize preview, import,
installation, grant changes, hiring, assignment, or spending.

## Platform

- `PlatformCapabilities.LlmChatStream` — `platform.llm.chat-stream.v1`
- `PlatformCapabilities.BusinessProfileRead` — `platform.business-profile.read.v1`
- `PlatformCapabilities.BusinessProfileUpdateExplicit` — `platform.business-profile.update-explicit.v1`
- `PlatformCapabilities.BusinessProfileProposeUpdate` — `platform.business-profile.propose-update.v1`
- `PlatformCapabilities.OrganizationSnapshotRead` — `platform.organization.snapshot.read.v1`
- `PlatformCapabilities.BusinessPatternSearch` — `platform.business-pattern.search.v1`
- `PlatformCapabilities.WorkstreamPlanPropose` — `platform.workstream.plan.propose.v1`
- `PlatformCapabilities.WorkforceSearch` — `platform.workforce.search.v1`
- `PlatformCapabilities.WorkforcePlanPropose` — `platform.workforce-plan.propose.v1`
- `PlatformCapabilities.FinanceProfileRead` — `platform.finance-profile.read.v1`
- `PlatformCapabilities.FinanceProfileProposeUpdate` — `platform.finance-profile.propose-update.v1`
- `PlatformCapabilities.BudgetEvaluate` — `platform.budget.evaluate.v1`
- `PlatformCapabilities.ApprovalPropose` — `platform.approval.propose.v1`
- `PlatformCapabilities.ManagementCycleRead` — `platform.management-cycle.read.v1`
- `PlatformCapabilities.UserInputRequest` — `platform.user-input.request.v1`
- `PlatformCapabilities.HiringRecommendationList` — `platform.hiring-recommendation.list.v1`
- `PlatformCapabilities.HiringRecommendationUpsert` — `platform.hiring-recommendation.upsert.v1`
- `PlatformCapabilities.HiringRecommendationResolve` — `platform.hiring-recommendation.resolve.v1`
- `PlatformCapabilities.HiringRecommendationWithdraw` — `platform.hiring-recommendation.withdraw.v1`
- `PlatformCapabilities.ResourceChangePropose` — `platform.management.resource-change.propose.v1`
- `PlatformCapabilities.ResourceChangeRead` — `platform.management.resource-change.read.v1`
- `PlatformCapabilities.ResourceChangeDecide` — `platform.management.resource-change.decide.v1`
- `PlatformCapabilities.HiringWorkflowStage` — `platform.hiring-workflow.stage.v1`
- `PlatformCapabilities.UserActionSuggest` — `platform.user-action.suggest.v1`

- `PlatformCapabilities.TeamRosterRead` — `platform.team-roster.read.v1`

`TeamRosterRead` is resolved from the authenticated installation to its active agent employee and
sole eligible team. It exposes bounded employee IDs, display names, human/agent type, company and
team roles, relationship, presence, and complete role-coverage counts. It never exposes email,
application-user IDs, installation or package identity, permissions, credentials, costs, prompts,
memory, or unrelated employees. Team membership does not grant any other authority.

## Memory

- `MemoryCapabilities.BusinessRead` — `memory.business.read.v1`
- `MemoryCapabilities.BusinessPropose` — `memory.business.propose.v1`
- `MemoryCapabilities.UserRead` — `memory.user.read.v1`
- `MemoryCapabilities.UserPropose` — `memory.user.propose.v1`

## Communication

- `CommunicationCapabilities.ChatRead` — `communication.chat.read.v1`
- `CommunicationCapabilities.ChatCreate` — `communication.chat.create.v1`
- `CommunicationCapabilities.ChatModify` — `communication.chat.modify.v1`
- `CommunicationCapabilities.ChatDelete` — `communication.chat.delete.v1`
- `CommunicationCapabilities.MessageSend` — `communication.message.send.v1`
- `CommunicationCapabilities.CoordinationStart` — `communication.coordination.start.v1`
- `CommunicationCapabilities.CoordinationRespond` — `communication.coordination.respond.v1`
- `CommunicationCapabilities.CoordinationRead` — `communication.coordination.read.v1`
- `CommunicationCapabilities.CoordinationList` — `communication.coordination.list.v1`
- `CommunicationCapabilities.CoordinationResume` — `communication.coordination.resume.v1`
- `CommunicationCapabilities.CoordinationCancel` — `communication.coordination.cancel.v1`
- `CommunicationCapabilities.MessageIngest` — `communication.message.ingest.v1`
- `CommunicationCapabilities.Send` — `communication.send.v1`
- `CommunicationCapabilities.WorkspaceApply` — `communication.workspace.apply.v1`
- `CommunicationCapabilities.IdentityAssign` — `communication.identity.assign.v1`
- `CommunicationCapabilities.LinkCodeRegister` — `communication.link-code.register.v1`

## Management and product leadership

- `ManagementCapabilities.CheckIn` — `management.check-in.v1`
- `ProductManagementCapabilities.RoleBrief` — `management.product-role-brief.v1`
- `ProductManagementCapabilities.PlanReview` — `management.product-plan.review.v1`
- `ProductManagementCapabilities.Escalation` — `management.product-escalation.v1`
- `ProductManagementCapabilities.Plan` — `product-management.plan.v1`
- `ProductManagementCapabilities.ContextUpdate` — `product-management.context.update.v1`

## Work management

New governed software-board mutations:

- `WorkBoardCapabilities.Configure` — `work.board.configure`
- `WorkBoardCapabilities.ConfigureColumns` — `work.board.columns.configure`
- `WorkOrchestrationCapabilities.ConfigureSoftwareTemplate` — `work.orchestration.software-template.configure`

Work-management capabilities require both an approved package capability and a live scoped grant
on the organization or board. SDK calls never bypass the board grant model. Mutation requests
include an idempotency key and, where applicable, the last observed resource revision.

- `WorkBoardCapabilities.Read` — `work.board.read`
- `WorkBoardCapabilities.Create` — `work.board.create`
- `WorkItemCapabilities.Read` — `work.item.read`
- `WorkItemCapabilities.Start` — `work.item.start`
- `WorkItemCapabilities.Create` — `work.item.create`
- `WorkItemCapabilities.FinalizeDelivery` — `work.item.delivery.finalize`
- `WorkItemCapabilities.Comment` — `work.item.comment`
- `WorkItemCapabilities.Estimate` — `work.item.estimate`
- `WorkItemCapabilities.Move` — `work.item.move`
- `WorkItemCapabilities.Complete` — `work.item.complete`
- `WorkItemCapabilities.Cancel` — `work.item.cancel`
- `WorkItemCapabilities.Reopen` — `work.item.reopen`
- `WorkItemCapabilities.Transfer` — `work.item.transfer`
- `WorkItemCapabilities.QualitySubmit` — `work.item.quality.submit`
- `WorkSprintCapabilities.Read` — `work.sprint.read`
- `WorkSprintCapabilities.Create` — `work.sprint.create`
- `WorkSprintCapabilities.Start` — `work.sprint.start`
- `WorkSprintCapabilities.Complete` — `work.sprint.complete`
- `WorkSprintCapabilities.Cancel` — `work.sprint.cancel`
- `WorkSprintCapabilities.ManageScope` — `work.sprint.scope.manage`
- `WorkSprintCapabilities.ManageCapacity` — `work.sprint.capacity.manage`
- `WorkSprintCapabilities.CarryOver` — `work.sprint.carryover`
- `WorkSprintCapabilities.ReadReports` — `work.sprint.report.read`
- `WorkAutomationCapabilities.Read` — `work.automation.read`
- `WorkAutomationCapabilities.Manage` — `work.automation.manage`
- `PersonalTodoCapabilities.Read` — `work.personal-todo.read.v1`
- `PersonalTodoCapabilities.Add` — `work.personal-todo.add.v1`
- `PersonalTodoCapabilities.Reorder` — `work.personal-todo.reorder.v1`
- `PersonalTodoCapabilities.Requeue` — `work.personal-todo.requeue.v1`
- `PersonalTodoCapabilities.Activate` — `work.personal-todo.activate.v1`
- `PersonalTodoCapabilities.Claim` — `work.personal-todo.claim.v1` (SDK runtime only)
- `PersonalTodoCapabilities.Complete` — `work.personal-todo.complete.v1` (SDK runtime only)
- `PersonalTodoCapabilities.Block` — `work.personal-todo.block.v1` (SDK runtime only)
- `PersonalTodoCapabilities.Release` — `work.personal-todo.release.v1` (SDK runtime only)
- `PersonalTodoCapabilities.Defer` — `work.personal-todo.defer.v1` (SDK runtime only)
- `PersonalTodoCapabilities.Update` — `work.personal-todo.update.v1`
- `PersonalTodoCapabilities.Archive` — `work.personal-todo.archive.v1`
- `PersonalTodoCapabilities.Restore` — `work.personal-todo.restore.v1`

### Board orchestration

- `WorkOrchestrationCapabilities.Read` — `work.orchestration.read`
- `WorkOrchestrationCapabilities.Preflight` — `work.orchestration.preflight`
- `WorkOrchestrationCapabilities.Start` — `work.orchestration.start`
- `WorkOrchestrationCapabilities.Pause` — `work.orchestration.pause`
- `WorkOrchestrationCapabilities.Resume` — `work.orchestration.resume`
- `WorkOrchestrationCapabilities.Cancel` — `work.orchestration.cancel`
- `WorkOrchestrationCapabilities.Retry` — `work.orchestration.retry`
- `WorkOrchestrationCapabilities.Execute` — `work.execution.run.v1`

## Source control and Git workspace

- `SourceControlCapabilities.TeamRepositoryOptions` — `source-control.repository.team-options.v2`
- `SourceControlCapabilities.ProvisionRepository` — `source-control.repository.provision.v2`

Git workspace operations are scoped to an assigned work item and its authoritative assignment
revision. Core derives the repository, base commit, and deterministic ticket branch. Agent
containers receive a credential-free tree without `.git`; provider credentials and installation
identifiers never appear in these contracts.

- `GitWorkspaceCapabilities.Prepare` — `git.workspace.prepare.v2`
- `GitWorkspaceCapabilities.Refresh` — `git.workspace.refresh.v2`
- `GitWorkspaceCapabilities.Inspect` — `git.workspace.inspect.v2`
- `GitWorkspaceCapabilities.Publish` — `git.workspace.publish.v2`
- `GitWorkspaceCapabilities.Cleanup` — `git.workspace.cleanup.v2`
- `GitMergeCapabilities.Review` — `git.merge.review.v2`
- `GitMergeCapabilities.Authorize` — `git.merge.authorize.v2`

## Web proxy

- `WebCapabilities.Fetch` — `web.fetch.v1`
- `WebCapabilities.Request` — `web.request.v1`
- `WebCapabilities.Render` — `web.render.v1`
- `WebCapabilities.Socket` — `web.socket.v1`

## Plugin runtime

- `PluginCapabilities.State` — `plugin.state.v1`

## Secure plugin operations

These provider-neutral broker capabilities keep credentials, provider upload sessions, and
operational storage outside plugin processes. Installations receive only the individual grants
declared and approved in their manifest.

- `CapabilityNames.ManagedActionExecute` — `platform.managed-action.execute.v1`
- `PlatformCapabilities.ManagedActionDecide` — `platform.managed-action.decide.v1` (restricted to the exact assigned agent-approver installation after a durable `com.csweet.managed-action.approval-requested.v1` event)
- `CapabilityNames.EngagementInboxUpsert` — `platform.engagement-inbox.upsert.v1`
- `CapabilityNames.MetricSnapshotWrite` — `platform.metric-snapshot.write.v1`
- `CapabilityNames.SynchronizationCheckpoint` — `platform.synchronization-checkpoint.v1`
- `CapabilityNames.MediaTransfer` — `platform.media.transfer.v1`

## Contribution rule

Every new platform capability must be added to the runtime registry and `CapabilityNames`, exposed
through an appropriate typed helper, included in `CapabilityCatalog.ByService`, documented here,
and covered by authorization, schema, quota, approval, and audit tests. Provider descriptors must
be valid, hashed manifest-v2 declarations.
