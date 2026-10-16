# Delegation and outcome tracking

SDK 4.4.0 adds shared interaction dispatch before specialist `HandleEventAsync` fallbacks.
Request capabilities through reviewed manifest grants; declarations do not expand resource or
execution authority.

```csharp
using CSweet.WorkManagement.Contracts;

var delegation = await context.Platform.Delegation.DelegateWithTrackingAsync(
    new DelegateWithTrackingRequest(recipientId, exactInstruction, intendedOutcome,
        sourceConversationId, sourceMessageId, $"delegation-source:{sourceMessageId:N}",
        InterestedParties: [requesterId], WorkContext: new(workstreamId, teamId)), token);
```

The instruction is an exact bounded selection from the retained source message. Shared classification
separates the outer request from quoted material and resolves current mentions/directory identities.
Clarify ambiguity. Persist before promising to ask the recipient and keep the requester updated.

`HandleDelegationReceivedAsync` returns typed `DelegationResponse.Acceptance`, `Clarification` or
`Decline`. Acceptance explains the intended action and optionally identifies owned canonical work.
The platform creates a management commitment when needed and runs accepted instructions through
the existing persisted conversation/lease path. `HandleDelegationExecutionAsync` defaults to the
specialist handler; `ResolveDelegationWorkAsync` binds the work actually addressing the request.
Preserve project-required development and existing approvals.

Default callbacks discover owned work on the assigned or requested project boards (at most eight
boards and 50 candidates), alongside personal work. The exact linked item is reread using its
current `WorkBoardId`, so transfers do not require guessing a new identity. Resource reads remain
grant-governed. `ReadDelegationRequest.ExecutionMessageId` locates a delegated turn independently
of the bounded outstanding list; durable `ExecutionHandled` receipts fence replay.

Shared runtime behavior handles coordinator reporting commitments independently of specialist task
handlers. `SummarizeDelegationUpdateAsync` uses one to three plain-language sentences and explicitly
shared evidence. The platform appends a link and commits informational report receipts without
response turns. Do not add polling loops or duplicate reports. Acknowledgement, forwarding, task
creation and requeueing are progress. `ReviewDelegationOutcomeAsync` needs authoritative terminal
evidence and a supported intended result before returning `FinalOutcome`.

`Platform.WorkTracking.FollowAsync` and `UnfollowAsync` use canonical item IDs independently of
delegation. `HandleWorkTrackingUpdateAsync` receives authorized current state; effects must be
idempotent. `PendingUpdatesAsync` and `MarkProcessedAsync` support bounded missed-event recovery.
Unfollowing and tracking cancellation leave execution under its current owner, even before
acknowledgement. `TrackingCancelled` describes reporting; `ExecutionStatus` independently describes
the recipient's request. Shared dispatch continues intake and execution while suppressing reporting.

Protocol names are defined by `DelegationCapabilities`. Receive and tracking-changed events are
wake hints: reread current state. Ordinary progress coalesces for five minutes; acknowledgements,
blockers, decisions and final reports are immediate. Provider failures leave durable state available
for retry/reconciliation. Existing untracked conversations remain compatible without these grants.

Store responsibilities, reporting destinations, linked work and delivery receipts in platform records.
Memory supplies authorized rationale/preferences, never workflow authority. Private case access is
not granted by interest in an outcome. Replacement, assignment fencing and private transfer remain
future platform work.
