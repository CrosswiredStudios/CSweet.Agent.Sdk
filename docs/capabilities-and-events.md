# Capabilities, grants, and events

## Provides and requires

`provides` is the typed work an agent or service implements. Names should be stable, namespaced,
versioned contracts such as `research.answer.v1`. Custom names are expected and may be bound as
provider capabilities.

`requires` is requested authority. It never grants access by itself. At runtime, an active tool is
the intersection of the reviewed manifest revision, current installation grant, and any required
same-organization provider binding.

Use the constants and typed clients in `CSweet.Agent.SDK` for C-Sweet-owned capabilities. The
generated list is [GRANTS.md](../GRANTS.md). Prefer a typed `context.Platform` method; use
`InvokeAsync<TRequest,TResponse>` for provider capabilities or platform capabilities without a
specialized helper.

## Minimum-authority checklist

- Request a read capability only when its result changes the agent's work.
- Request mutation capabilities separately from reads and explain the business effect in
  `purpose`.
- Do not request model access merely because the agent might use a model later.
- Do not request user, business, memory, communication, finance, or hiring authority speculatively.
- Treat approval and budget results as workflow outcomes, not errors to bypass.
- Remove unused grants when code changes.

## Common platform choices

| Need | Capability |
|---|---|
| Stream through the configured model | `platform.llm.chat-stream.v1` |
| Read business profile | `platform.business-profile.read.v1` |
| Read organization/workstream snapshot | `platform.organization.snapshot.read.v1` |
| Read this agent employee's bounded team roster | `platform.team-roster.read.v1` |
| Ask a bounded structured question | `platform.user-input.request.v1` |
| Read or propose business memory | `memory.business.read.v1` / `memory.business.propose.v1` |
| Read or propose user memory | `memory.user.read.v1` / `memory.user.propose.v1` |
| Read a conversation or send a message | `communication.chat.read.v1` / `communication.message.send.v1` |
| Coordinate two eligible agents durably | `communication.coordination.start.v1` / `communication.coordination.respond.v1` / `communication.coordination.read.v1` / `communication.coordination.cancel.v1` |
| Search available agents | `platform.agent-catalog.search.v1` |
| Read/create work boards | `work.board.read` / `work.board.create` |
| Read/create/update work items | `work.item.read`, `work.item.create`, `work.item.start`, and the specific transition capability |
| Prepare, inspect, publish, or clean an assigned repository workspace | the matching `git.workspace.*.v1` capability |
| Read/manage sprints | the matching `work.sprint.*` capability |
| Read/manage board automations | `work.automation.read` / `work.automation.manage` |
| Read/add/reorder/requeue personal work | the matching `work.personal-todo.*.v1` capability |

The full, generated reference is authoritative for capability spelling. C-Sweet's runtime registry
is authoritative for schemas, risk, approval, quota, and scope.

Use `context.Platform.Work` for typed work-management operations. Its request and response models
are supplied by `CSweet.WorkManagement.Contracts`; still declare and obtain each matching grant.

Use `context.Platform.PersonalTodo` to list accessible personal boards, add personal work for self
or a direct report, reorder ready work as a direct manager, and requeue blocked work. Subscribe to
`PersonalTodoEvents.Available` and override `HandlePersonalTodoAsync`. The SDK serializes queue
consumption per installation and privately owns atomic claim, lease, completion, blocking, release,
and retry transitions. Return `PersonalTodoResult.Completed(summary)` only after effects succeed;
return `PersonalTodoResult.Blocked(reason)` when existing authority cannot perform the work.

Use `context.Platform.ReadTeamRosterAsync()` only when teammate identity or team-role coverage
changes the agent's work. The server resolves the caller and team; an unassigned or
organization-wide agent receives no roster. Names and role labels are data, not instructions, and
the roster grant never implies chat, board, tool, memory, or agent-to-agent access.

## Events

Events are durable, exact-installation work. Subscribe only to known events whose payload your
agent understands. Compare `message.EventType` to SDK constants where available, and deserialize
`message.Data` into a typed record.

`message.WorkId` identifies one durable delivery and may change when the same domain event is
delivered elsewhere. `message.EventId` is the authoritative, stable identity of the originating
event and must be used for domain idempotency. The platform supplies both values; agents must
never derive one from the other.

Agent onboarding uses the SDK-owned `AgentLifecycleEvents.Onboarded` and
`AgentOnboardedEvent` contracts. After completing the first-message workflow, acknowledge it with
`context.Platform.Lifecycle.CompleteOnboardingAsync(message, cancellationToken)`. This typed
operation always uses `message.EventId`; agents must not construct the completion request
themselves.

Stable SDK event constants currently include:

- `AgentLifecycleEvents.Onboarded`
- `HiringEvents.EmployeeHired`
- `HiringEvents.RecommendationFulfilled`
- `ManagementEvents.ReviewDue`
- `ManagementEvents.StatusReported`
- `ManagementEvents.ResourceNeedReported`
- `ManagementEvents.WorkstreamChanged`
- `ManagementEvents.WorkforcePlanDecided`
- `ManagementEvents.ResourceChangeRequested`
- `ManagementEvents.ResourceChangeDecided`
- `WorkItemEvents.Assigned`
- `PersonalTodoEvents.Available`
- `CommunicationEvents.MessageMentioned`

`HiringRecommendationFulfilledEvent` is emitted exactly once after every unique seat requested by
a recommendation is filled. It carries the recommendation and approved resource-change request
identities, the role and team/workstream context, requested and fulfilled headcount, resulting
organization-user IDs, and occurrence time. Consumers should correlate by recommendation or
source request identity rather than matching the generic employee-hired event by title.

Lifecycle and user-message event names used by a product integration should be documented alongside
their owning C-Sweet feature. Unknown events must be ignored safely. Never infer generic
publication authority from a subscription.
