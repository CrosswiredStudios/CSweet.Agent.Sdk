# Staffing reviews and exact waits

`StaffingWorkDescription` contains judgment-bearing purpose, timing, accountability, engagement and a named `Requester` or `Manager` reporting relationship. `Apply` merges this judgment into a code-owned `ResourceChangeRole`. It preserves full narrative text and never creates capability grants or approval authority. The work and actual coverage should justify a role and its activation; future needs need not become immediate hires.

Use `StaffingReviews.Latest` over an authorized resource-context read, supplying the exact requester, current manager, team and project. An optional predicate binds the applicable accepted source revision. Each returned review can receive a new revision. Duplicate deliveries of one review must reuse the existing replacement, not generate another plan.

`ResourceChangeProposalRequest.RevisesRequestId` identifies the returned or rejected review being answered. `SupersedesRequestId` still identifies the approved baseline used for staffing deltas. Preserve existing occupied roles. The matching platform enforces scope and one replacement per returned request, even if a response is interrupted and a caller supplies another command key.

When waiting on a pending review, use:

```csharp
return PersonalTodoResult.WaitingUntil(nextReviewAt, explanation,
    request.ManagerOrganizationUserId, StaffingReviews.Dependency(request));
```

The public JSON dependency is `{ "kind": "resource-change.v1", "resourceId": "<request-id>", "observedVersion": "Pending" }`. Third-party agents can send it through `work.personal-todo.defer.v1` without using this SDK. It is optional for older agents. The platform validates owner, installation and exact team/project against current state, atomically saves wakes and their outbox events, and reconciles missed notifications. Events are hints; the resumed agent reads current facts before proposing or scheduling work. Waking a task grants no sprint or spending authority.

Planning should assess the newest applicable review rather than replay the first cached assessment. Keep the model correction bound for an invalid command separate from the number of legitimate manager reviews. A platform validation rejection is feedback once; if correction fails, retain an actionable owner and wait for changed evidence or configuration. A rejected draft is never an accepted command.
