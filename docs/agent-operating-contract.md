# Authoring agents under the operating contract

Protocol-v2 agent manifests may declare a role policy:

```json
"rolePolicy": {
  "profile": "manager.v1",
  "declaredRoleKeys": ["software-product-manager"]
}
```

Supported profiles are `manager.v1`, `individual-contributor.v1`, `independent-reviewer.v1`, and `executive-advisor.v1`.

Use `requires[].modelVisible: false` when agent code needs a granted capability but the configured model must not receive that tool. Model tools are derived from approved manifest requirements and effective provider bindings; code should not load a broad tool set and filter it by function name.

Continuous agents should send startup, recovery, periodic, and `StateChanged` attention through one reconciler. Read authoritative systems on every cycle, then store the resulting previous assessment with `ReadOperatingStateAsync` and `WriteOperatingStateAsync`. Writes use an expected revision and idempotency key; on conflict, reread and reassess.

Memory is supporting narrative context only. Assignments, approvals, staffing viability, workflow state, grants, and replay safety remain platform-owned.
