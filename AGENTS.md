# C-Sweet Agent SDK contributor instructions

## Repository purpose

This repository is the canonical .NET authoring surface for C-Sweet protocol-v2 agents and
service plugins. Agent implementations use callbacks and typed platform clients. The SDK alone
owns the private MCP transport, workload authentication, sessions, work leases, retries, progress
sequencing, and shutdown.

When asked to create a new C-Sweet agent, read and follow
[`AGENT_AUTHORING.md`](AGENT_AUTHORING.md). Use the `csweet-agent` template; do not improvise a
different repository layout when the template can express the request.

## Non-negotiable boundaries

- Never expose MCP, JSON-RPC, workload or session tokens, lease tokens, runtime endpoints, or
  transport clients through public authoring APIs.
- Never add direct database, Docker socket, host filesystem, provider credential, or unrestricted
  network access to an agent.
- Manifest declarations request authority; they do not grant it. Generated agents request the
  minimum capabilities and network access needed for their stated purpose.
- Protocol-v2 agents use `csweet-plugin.json`, `manifestVersion` 2.0, protocol 2.0 through 2.x,
  and a `dotnet-project` runtime. Do not reintroduce `csweet-agent.json`, gRPC, protobuf, generic
  event publication, or caller-selected installation IDs.
- Custom provider capabilities are valid. `CapabilityCatalog` documents C-Sweet-owned capability
  names; it is not an allow-list for `provides` or provider-bound `requires`.
- Work is delivered at least once. Agent callbacks must honor cancellation and make external
  effects idempotent using a stable domain key. `work-item` idempotency deduplicates one durable
  work item; a request contract's `IdempotencyKey` protects a downstream domain effect.

## Sources of truth

- Agent creation contract: `AGENT_AUTHORING.md`
- Human tutorial: `docs/creating-an-agent.md`
- Manifest reference and schema: `docs/manifest-reference.md` and
  `schemas/csweet-plugin.v2.schema.json`
- Capability and event guidance: `docs/capabilities-and-events.md` and `GRANTS.md`
- Testing/import/release: `docs/testing-and-release.md`
- Runtime internals: `docs/runtime-maintainers.md`

Keep the SDK version synchronized in the project, README, template defaults, sample, and package
tests. Keep manifest validation synchronized with C-Sweet's protocol-v2 importer. Documentation
examples must compile or be exercised by tests.

## Required verification

Run from the repository root:

```powershell
dotnet test CSweetAgentSdk.slnx
dotnet pack src/CSweet.Agent.SDK/CSweet.Agent.SDK.csproj -c Release
```

Changes to the authoring API, manifest contract, template, or documentation require tests covering
the happy path and relevant invalid inputs. Template changes must pass the temporary-repository
generation test. Security-boundary changes require updating `SECURITY.md` and
`docs/runtime-maintainers.md`.
