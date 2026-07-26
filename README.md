# C-Sweet Agent SDK

`CSweet.Agent.SDK` 1.0 is the transport-neutral .NET authoring API for C-Sweet agents and service plugins. Implement callbacks and use typed platform clients; the SDK privately manages the outbound runtime, authentication, live grants, durable work, retries, progress, and shutdown.

```powershell
dotnet add package CSweet.Agent.SDK --version 1.0.0
```

```csharp
using CSweet.Agent.SDK;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);
builder.AddCSweetAgent<MyAgent>();
await builder.Build().RunAsync();

sealed class MyAgent : CSweetAgentBase
{
    public override string AgentId => "com.example.my-agent";
    public override string Version => "1.0.0";
}
```

Start with [Creating an agent](docs/creating-an-agent.md). Existing agents should follow [Migrating to 1.0](docs/migrating-to-1.0.md). Runtime implementation rules are in [Runtime maintainers](docs/runtime-maintainers.md), and the generated grant reference is [GRANTS.md](GRANTS.md).

`AgentRuntimeContext.Platform` exposes typed, grant-governed services. `GetModelToolsAsync()` returns the current model-visible grant. `CreateChatClient()` provides platform-governed model streaming. `AgentTestRuntime` runs agent callbacks and fake capabilities entirely in memory.

An agent never receives provider credentials, database access, a caller-selected target installation, raw runtime tokens, transport clients, or queue/lease details. Manifests request authority; the active installation grant and live provider bindings remain authoritative.

```powershell
dotnet test CSweetAgentSdk.slnx
dotnet run --project samples/HelloAgent -- --self-test
dotnet pack src/CSweet.Agent.SDK/CSweet.Agent.SDK.csproj -c Release
```

Report security issues privately to the maintainers.
