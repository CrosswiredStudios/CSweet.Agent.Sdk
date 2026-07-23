# Authoritative capability catalog

`CSweet.Agent.SDK.CapabilityNames` is the authoritative source for serialized C-Sweet capability
and grant names. Agent and service code must use the typed constants exposed by the SDK rather than
repeat wire-name strings.

`CapabilityCatalog.ByService` provides the same names grouped by owning service or feature, while
`CapabilityCatalog.All` and `CapabilityCatalog.IsKnown(...)` support manifest validation and audit
tests.

The broker remains authoritative for whether a known capability is global, requested, granted, and
allowed for a particular installation. Being listed here does not grant access.

## Assistant

- `AssistantCapabilities.Converse` — `assistant.converse.v1`
- `AssistantCapabilities.SummarizeActivity` — `assistant.summarize-activity.v1`
- `AssistantCapabilities.PlanWork` — `assistant.plan-work.v1`

## Agent lifecycle and configuration

- `AgentConfigurationCapabilities.Describe` — `agent.configuration.describe.v1`
- `AgentConfigurationCapabilities.Update` — `agent.configuration.update.v1`
- `AgentLifecycleCapabilities.CompleteOnboarding` — `agent.onboarding.complete.v1`

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
- `PlatformCapabilities.HiringWorkflowStage` — `platform.hiring-workflow.stage.v1`

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

## Web proxy

- `WebCapabilities.Fetch` — `web.fetch.v1`
- `WebCapabilities.Request` — `web.request.v1`
- `WebCapabilities.Render` — `web.render.v1`
- `WebCapabilities.Socket` — `web.socket.v1`

## Plugin runtime

- `PluginCapabilities.State` — `plugin.state.v1`

## Contribution rule

Every new capability must be added to `CapabilityNames`, exposed through an appropriate typed
helper, included in `CapabilityCatalog.ByService`, documented here, and used by manifest-audit
tests before an agent or service requests or provides it.
