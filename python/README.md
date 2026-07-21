# C-Sweet Python runtime helper

Generate the gRPC client from `agent_broker.proto`, register the container, then create `McpConnectionInfo` from the accepted registration result. `create_maf_tool()` supplies the dedicated MCP bearer credential through MAF's `header_provider`; the credential never becomes a prompt or tool argument.

When `expires_at` is reached, reconnect to the broker to obtain a new session-bound credential. Tool visibility is still filtered and re-authorized by the C-Sweet broker on every call.

Registration reports `global_capabilities` separately from `granted_requested_capabilities`.
Global tools, currently `ask_user`, require no package-manifest grant but still require a live,
authenticated installation and pass through the same broker authorization path.

`AgentIdentity.from_registration()` returns the organization employee identity assigned at hire,
including the hired name, role, responsibilities, authority level, and manager. The same value is
available as `McpConnectionInfo.identity`. It is `None` for an installation that has not been hired
or when connecting to an older broker.
