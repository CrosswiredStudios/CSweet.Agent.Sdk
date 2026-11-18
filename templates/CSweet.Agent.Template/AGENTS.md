# Example Agent repository instructions

This repository contains one standalone C-Sweet protocol-v2 agent. Its purpose is:

> Processes a bounded request for C-Sweet.

## Invariants

- Keep `com.example.agent` and version `0.1.0` synchronized between agent code,
  `csweet-plugin.json`, tests, and releases.
- The root manifest is the reviewed authority request. Keep `provides`, `requires`, events,
  configuration, credentials, web access, and UI contributions synchronized with implementation
  and tests.
- Request the minimum authority needed. Manifest declarations never grant access.
- Use typed callbacks and `AgentRuntimeContext.Platform`. Do not implement MCP/JSON-RPC, access
  runtime/workload/lease tokens, connect directly to databases or Docker, or handle provider
  credentials.
- Agent work is delivered at least once. Honor cancellation and use stable domain idempotency keys
  for external mutations.
- Unknown capabilities and events must fail or be ignored safely without leaking sensitive data.

## Verification

Every agent-flow fix includes a regression using the real agent in `CSweet.Agent.Testing.AgentTestHost`. Exercise the callback that failed, script its external inputs, and assert the durable effect that allows the next participant to proceed. For restart or redelivery, use a new agent instance over retained platform state, replay the original delivery ID and assert a single accepted effect.

Add or extend a test marked `[Trait("Category", "AgentHarness")]`. Run the affected scenario and the full suite. Report the scenario name, executed count and before-and-after outcome. A cross-agent fix also exercises the receiving agent with the sending agent's actual output in the workflow suite.

Run from the repository root:

```powershell
dotnet test
dotnet run --project src/CSweet.Agent.Template -- --self-test
```

Any new capability, grant, event, configuration field, credential, or network rule requires a
manifest update, a README explanation, and tests.
