# C-Sweet Agent SDK

`CSweet.Agent.SDK` is the public .NET SDK for building broker-governed C-Sweet agents.
Agents register with C-Sweet, receive authorized events and capability requests, and access
platform services without receiving database credentials or provider secrets.

## Install

```powershell
dotnet add package CSweet.Agent.SDK --version 0.2.0
```

Create an executable host, implement `ICSweetAgent` (or derive from `CSweetAgentBase`), and call:

```csharp
builder.AddCSweetAgent<MyAgent>();
```

The repository should contain a root `csweet-plugin.json` using the canonical `kind`, `provides`,
`requires`, and `events` schema. Legacy `csweet-agent.json` manifests remain readable for one
compatibility release. For source imports, set `runtime.type` to `dotnet-project` and
`runtime.projectPath` to the relative project path.

## Agent authoring guide

Start with [Creating a C-Sweet agent](docs/creating-an-agent.md). It covers project structure,
manifest grants, event handling, the required onboarding acknowledgement, idempotent side effects,
bounded platform retries, failure notifications, and a practical test checklist.

## Broker-governed platform tools

`AgentRuntimeContext.Platform` exposes typed clients for authoritative business and finance
profiles, organization snapshots, patterns, workstream and workforce proposals, budgets,
approvals, and management cycles. `PlatformToolAdapters.Create(context.Platform)` converts the
same clients into Microsoft Agent Framework tools; extensions do not need to reimplement JSON or
authorization behavior.

Platform failures use stable codes including `Denied`, `Unavailable`, `NotFound`, `Conflict`,
`ValidationFailed`, `ApprovalRequired`, and `BudgetExceeded`. Workforce catalogs plug in through
`IWorkforceCatalogProvider`; disconnected marketplaces must return an unavailable result rather
than synthetic candidates.

Agents can implement `IAgentActivationHandler` to distinguish interactive, scheduled, manual,
and always-on activation, or `IAgentConnectedService` for work that should run while connected.

Management review and status-report contracts include additive executive-briefing fields for a
durable request identifier, concise Markdown, immediate actions, conversation topics, operating
signals, budget position, and briefing schedule. Older payloads remain deserializable with empty
briefing fields.

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

### Creating NuGet packages

Run the batch file from the repository root to restore dependencies, run the test suite, and create the package and symbols package in a versioned directory such as `artifacts\packages\0.2.0`:

```bat
Create-NuGetPackages.bat
```

Pass a version and optional output root to override the project defaults. The version directory is appended automatically:

```bat
Create-NuGetPackages.bat 0.2.0 C:\packages\csweet-agent-sdk
```

Pushing a `v*` Git tag runs `.github\workflows\publish.yml`, which tests, packages, and publishes to NuGet.org using the repository's `NUGET_API_KEY` secret.

## Security

Manifests declare requested access; C-Sweet installation policy remains authoritative. Report
security issues privately to the repository maintainers rather than opening a public issue.
