# C-Sweet Agent SDK

`CSweet.Agent.SDK` is the public .NET SDK for building broker-governed C-Sweet agents.
Agents register with C-Sweet, receive authorized events and capability requests, and access
platform services without receiving database credentials or provider secrets.

## Install

```powershell
dotnet add package CSweet.Agent.SDK --version 0.1.0
```

Create an executable host, implement `ICSweetAgent` (or derive from `CSweetAgentBase`), and call:

```csharp
builder.AddCSweetAgent<MyAgent>();
```

The repository must contain a root `csweet-agent.json`. For source imports, set
`runtime.type` to `dotnet-project` and `runtime.projectPath` to the relative project path.

## Broker-mediated LLM access

Create `BrokerLlmClient` with the current `AgentRuntimeContext.Broker` and an
`AgentLlmSelection`. The SDK sends `platform.llm.chat-stream.v1` to the trusted platform and
returns model deltas as `IChatClient` streaming updates. Raw model-provider credentials never
enter the agent process.

## Development

```powershell
dotnet test CSweetAgentSdk.slnx
dotnet pack src/CSweet.Agent.SDK/CSweet.Agent.SDK.csproj -c Release
```

## Security

Manifests declare requested access; C-Sweet installation policy remains authoritative. Report
security issues privately to the repository maintainers rather than opening a public issue.
