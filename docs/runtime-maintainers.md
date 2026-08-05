# Runtime maintainer guide

This document describes SDK internals. Agent authors should use the callback API and must not depend on these details.

The SDK uses private Streamable HTTP `/mcp`. `initialize` reads a one-use workload token from the configured secret file. It sends runtime, tick, installation, organization, package identity, and version metadata. The returned token is held only in memory, renewed after five minutes, and discarded on disconnect.

The worker long-polls `csweet/work/claim` for at most 25 seconds, runs bounded concurrent callbacks, renews each 60-second lease every 20 seconds, reports bounded progress, and completes/fails using the attempt and lease token. Event leases carry both the delivery `workId` and the originating domain `eventId`; the worker rejects event work without the latter. It reconnects with bounded jitter and never invents work from notifications.

`tools/list` is the only descriptor source. The SDK caches descriptors only by grant revision. Typed calls still resolve a live descriptor and the gateway reauthorizes every call. `modelVisible` controls conversion to `AITool`; transport and lifecycle tools are never exposed to models.

Git workspace v2 typed clients intentionally omit repository and ref selection. Do not add clone
URLs, connection IDs, provider installation IDs, credentials, branches, base refs, or arbitrary
commit SHAs to prepare requests. The authoritative assignment revision is required on every
workspace operation. `PlatformSourceControlClient` exposes only bounded repository intent and
exact-SHA decisions; it is not a general GitHub administration client.

Source-control provider authentication belongs to separately deployed trusted services. SDK code
must never inspect `.git`, inject credentials, invoke authenticated Git, mint provider tokens, or
offer a local fallback. A provider outage is reported as a bounded platform capability failure.

Runtime methods:

- `csweet/session/renew`
- `csweet/work/claim`
- `csweet/work/renew`
- `csweet/work/progress`
- `csweet/work/complete`
- `csweet/work/fail`
- `csweet/runtime/complete`

Never expose session/workload/lease tokens, the endpoint, `HttpClient`, JSON-RPC, or MCP objects through public authoring APIs. Transport interfaces remain internal. Tests use `AgentTestRuntime`, which deliberately models callbacks and capability grants rather than wire details.

Protocol extensions must be additive within 2.x, server-advertised, size-bounded, authenticated, non-model-visible, and covered by replay/restart/cancellation tests. A change to identity, authorization, lease, completion, or credential semantics requires a new protocol minimum and coordinated security review.

Manifest `connections` and `setup` are additive authoring contracts, not authority. The importing
platform must repeat validation, resolve provider profiles from its trusted registry, render only
known native components, and restrict setup callbacks to a bootstrap grant containing exactly the
declared setup capabilities. The normal agent runtime, model access, memory, chat, organization
data, filesystem, and ordinary network grants remain unavailable until platform activation gates
transition the installation to ready.
