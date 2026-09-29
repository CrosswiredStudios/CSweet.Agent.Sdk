# Manager agents

Product Manager and Video Game Producer are both managers to C-Sweet. The common type is declared
in `csweet-plugin.json`; the specialized job and skills are separate metadata:

```json
"rolePolicy": {
  "baseType": "manager",
  "profile": "manager.v1",
  "declaredRoleKeys": ["manager", "product-manager"],
  "specializationKeys": ["software-delivery"]
}
```

A video-game Producer uses the same `baseType` and `profile`, with `game-producer` as its specialized
role and `video-game-development` among its specializations. Future manager job names require no
platform code changes. `baseType` must match the policy profile. Older manifests without `baseType`
retain their family through `profile`; new manifests should declare it explicitly. Neither declaration
grants access or makes the installation responsible for every project.

Derive the agent from `CSweetManagerAgentBase` and supply `ManagementResponsibility`. Put ordinary
events in `HandleManagerEventAsync` and ordinary attention work in `HandleManagerAttentionReviewAsync`.
The sealed public callbacks process incident events and bounded reconnect discovery before those
specialized workflows. Existing capability, coordination and personal-work hooks remain available.

```csharp
public sealed class ProductManagerAgent : CSweetManagerAgentBase
{
    public override string AgentId => "example.product-manager";
    public override string Version => "1.0.0";
    protected override string ManagementResponsibility => "software product planning and delivery";
}
```

Request these capabilities in the manifest's **root** `requires` array and approve them through the
normal installation grant review:

- `platform.project-health.read.v1`
- `platform.project-health.diagnostics.read.v1`
- `platform.management.incident.read.v1`
- `platform.management.incident.report.v1`
- `platform.management.incident.forward.v1` when explicit forwarding is also needed

Subscribe to `com.csweet.project-health.review-due.v1`, `com.csweet.management.incident.changed.v1`,
and `com.csweet.agent.attention.review-due.v1`. The platform selects the monitoring owner from existing
project assignments: an explicit `project-health-manager` supervision assignment, a project board
manager, another assigned supervisor, then the accountable project manager. Only manager-typed agent
installations qualify. The actual reporting hierarchy determines escalation recipients.

## Role-specific decisions

Override `AssessIncidentAsync(ManagerIncidentContext, AgentRuntimeContext, CancellationToken)` to
assess the authoritative incident and its bounded, sanitized diagnostics. The default preserves the
evidence and escalates when it cannot establish an authorized resolution within its responsibility.

The hook can use existing typed, granted operations to request a permitted recovery. Use
`ManagerIncidentContext.ActionIdempotencyKey` plus a stable action suffix for each external mutation;
the key remains stable across deliveries and incident revisions. Do not use a wake revision as the
idempotency key for a repair. All normal approval, scope and execution checks still apply.

Return one of:

- `ManagerIncidentAssessment.Escalate(reason)` to forward up the actual hierarchy.
- `Investigating(reason, reviewAt)` for a bounded evidence or dependency follow-up.
- `AwaitingRecovery(reason, reviewAt, actionReference)` after recording an existing authorized
  recovery request. The action reference is a report, not proof that recovery succeeded.

Follow-ups must be in the future and before the current escalation deadline. They are persisted and
delivered by the platform, so agents do not poll. Neither acknowledgement, investigation nor a claimed
recovery extends the 15-minute hop deadline or closes an incident. Authoritative recovery/cancellation
evidence closes it. No retry or repair authority is granted by inheriting this class.

Diagnostic access or assessment failure still produces a deterministic baseline report. No diagnostic
evidence is sent to an LLM by the shared manager behavior. `ManagerAgentTests` demonstrates a new
manager with a custom assessment hook, stable recovery keys, and failure fallback.

The older `ProjectIncidentReview.HandleAsync` helper remains available for SDK compatibility.
`ManagementIncident.MonitoringManagerEmployeeId` is the generic owner accessor; the legacy
`ProducerEmployeeId` wire field remains readable by older SDK clients.
