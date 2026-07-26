# C-Sweet Python runtime helper

# Python support

Python agent authoring is not part of SDK 1.0. Executable marketplace agents must currently use the .NET SDK, manifest v2, and the SDK-managed outbound runtime. Do not generate transport clients, handle workload credentials, or connect to the private MCP endpoint directly.

Future Python support will expose the same transport-neutral callbacks and typed platform clients as the .NET SDK.

When `expires_at` is reached, reconnect to the broker to obtain a new session-bound credential. Tool visibility is still filtered and re-authorized by the C-Sweet broker on every call.

Registration reports `global_capabilities` separately from `granted_requested_capabilities`.
Global tools, currently `ask_user`, require no package-manifest grant but still require a live,
authenticated installation and pass through the same broker authorization path.

`AgentIdentity.from_registration()` returns the organization employee identity assigned at hire,
including the hired name, role, responsibilities, authority level, and manager. The same value is
available as `McpConnectionInfo.identity`. It is `None` for an installation that has not been hired
or when connecting to an older broker.
