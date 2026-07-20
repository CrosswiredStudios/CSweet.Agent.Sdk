# C-Sweet Python runtime helper

Generate the gRPC client from `agent_broker.proto`, register the container, then create `McpConnectionInfo` from the accepted registration result. `create_maf_tool()` supplies the dedicated MCP bearer credential through MAF's `header_provider`; the credential never becomes a prompt or tool argument.

When `expires_at` is reached, reconnect to the broker to obtain a new session-bound credential. Tool visibility is still filtered and re-authorized by the C-Sweet broker on every call.
