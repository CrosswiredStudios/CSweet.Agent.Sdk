# C-Sweet Agent SDK

`CSweet.Agent.SDK` 2.6.0 is the supported .NET 10 authoring API for C-Sweet agents and service
plugins. You implement typed callbacks; the SDK privately manages the outbound runtime,
authentication, live grants, durable work, retries, progress, and shutdown.

## Create an agent

Install the template directly from a checkout of this repository:

```powershell
dotnet new install ./templates/CSweet.Agent.Template
dotnet new csweet-agent --name ResearchAgent `
  --AgentId com.example.research-agent `
  --DisplayName "Research Agent" `
  --PublisherId com.example `
  --PublisherName "Example" `
  --AgentVersion 0.1.0 `
  --PrimaryCapability research.answer.v1 `
  --SdkVersion 2.6.0
cd ResearchAgent
dotnet test
```

To author without the template, add the package directly:

```powershell
dotnet add package CSweet.Agent.SDK --version 2.6.0
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
    public override string Version => "0.1.0";
}
```

## Start here

- Humans: [Creating an agent](docs/creating-an-agent.md)
- Codex and other coding agents: [Agent authoring contract](AGENT_AUTHORING.md)
- Manifest fields: [Manifest reference](docs/manifest-reference.md) and
  [JSON Schema](schemas/csweet-plugin.v2.schema.json)
- Grants and events: [Capabilities and events](docs/capabilities-and-events.md) and
  [generated capability reference](GRANTS.md)
- Import and release: [Testing and release](docs/testing-and-release.md)
- Existing protocol-v1 agents: [Migrating to 1.0](docs/migrating-to-1.0.md)
- Existing SDK 1.x agents: [Migrating to SDK 2.0](docs/migrating-to-2.0.md)

`AgentRuntimeContext.Platform` exposes typed, grant-governed services.
`GetModelToolsAsync()` returns the current model-visible grant.
`CreateChatClient()` provides platform-governed model streaming.
`AgentTestRuntime` runs callbacks and fake capabilities entirely in memory.
Git workspace responses support exact-commit preparation, published-branch resumption,
tracked-change inspection, and governed merge status metadata (`None`, `Queued`, `Merged`,
or `Blocked`).

An agent never receives provider credentials, database access, a caller-selected target
installation, raw runtime tokens, transport clients, or queue/lease details. Manifests request
authority; installation grants and live provider bindings remain authoritative.

## SDK development

```powershell
dotnet test CSweetAgentSdk.slnx
dotnet run --project samples/HelloAgent -- --self-test
dotnet pack src/CSweet.Agent.SDK/CSweet.Agent.SDK.csproj -c Release
```

Runtime implementation rules are in [Runtime maintainers](docs/runtime-maintainers.md). Report
security issues privately to the maintainers as described in [SECURITY.md](SECURITY.md).
