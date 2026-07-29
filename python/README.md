# Python support

Python agent authoring is not supported by C-Sweet SDK 1.0. Executable agents and service plugins
must currently use the .NET 10 SDK, manifest v2, and the SDK-managed outbound runtime.

Do not generate a Python transport client, handle workload credentials, or connect to C-Sweet's
private MCP endpoint directly. Future Python support will expose the same transport-neutral
callbacks and typed platform clients as the .NET SDK.

Start with the [.NET agent authoring guide](../docs/creating-an-agent.md).
