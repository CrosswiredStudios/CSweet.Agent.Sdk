# Create a standalone C-Sweet agent

This is the portable build contract for Codex and other coding agents. It is authoritative when a
user points to this SDK from another repository.

## Outcome

Create one independently buildable .NET 10 repository containing:

- a root `csweet-plugin.json`;
- an executable agent project under `src/`;
- an xUnit test project under `tests/`;
- a solution, human README, and repository-scoped `AGENTS.md`;
- no source-tree reference back to this SDK repository.

The result is complete only when `dotnet test` passes and the manifest can be loaded by
`AgentManifestLoader`.

Inspect the current directory before generating. If it already contains an unrelated project,
ask for or create an explicitly approved new directory for the standalone agent. Never apply the
template with forced overwrites to an existing repository.

## Inputs to resolve

Before generating files, determine these values from the request or ask only when they cannot be
safely inferred:

| Input | Rule |
|---|---|
| Agent name and purpose | One concise business responsibility |
| Agent ID | Stable reverse-DNS identifier, for example `com.example.research-agent` |
| Publisher ID/name | Stable owner identity shown during installation |
| Version | Semantic version; use `0.1.0` for a new unpublished agent |
| Primary capability | Namespaced `<domain>.<action>.v1` contract implemented by the agent |
| Events | Subscribe only to events the purpose requires |
| Required capabilities | Request the minimum platform/provider authority needed |

Default to `OnDemand` activation, one concurrent job, multiple installations supported, no
configuration, no credentials, and `webAccess.mode` `None`.

## Generation workflow

1. Install the repository template from this SDK path:

   ```powershell
   dotnet new install <SDK_PATH>/templates/CSweet.Agent.Template
   ```

2. Generate into the target repository:

   ```powershell
   dotnet new csweet-agent `
     --name <DotNetName> `
     --AgentId <reverse-dns-id> `
     --DisplayName "<display name>" `
     --PublisherId <publisher-id> `
     --PublisherName "<publisher name>" `
     --AgentVersion <semantic-version> `
     --PrimaryCapability <capability.v1> `
     --SdkVersion 3.12.0
   ```

3. Replace the template request/response contract and handler with purpose-specific typed
   contracts. Keep a safe unsupported-capability result.
4. Add only required `requires` entries and event subscriptions. Every declared capability,
   event, configuration field, credential, and web rule must be used by code and covered by a
   test.
5. Add configuration through `CSweetAgentBase.Configure`. The signed manifest is the schema, so
   settings do not require runtime describe/update capabilities. Override
   `OnConfigurationChangedAsync` only when live changes need agent-specific resource handling.
6. Use typed `context.Platform` methods for C-Sweet operations. Use
   `context.CreateChatClient(...)` and `context.GetModelToolsAsync()` for platform-governed model
   access. Never create a provider client with a credential.
7. Preserve the security boundaries in the generated `AGENTS.md`.
8. Run `dotnet test` from the generated repository root.

For personal queue support, request only the personal-todo capabilities the installation needs,
subscribe to `com.csweet.work.personal-todo.available.v1`, and override
`HandlePersonalTodoAsync`. Claim identifiers and leases are SDK-private. The callback must return
`PersonalTodoResult.Completed(...)`, `PersonalTodoResult.InProgress(...)`, or
`PersonalTodoResult.Blocked(...)`; it must not move queue cards directly. `InProgress` releases
the transient claim while retaining the card in Doing until an external event resumes it.
Subscribed agents also sweep their queue once when a runtime session connects, so
a wake event missed during an installation upgrade cannot strand ready work. Mention identities on
`PersonalTodoItem.Mentions` come from validated source messages or structured ticket spans and may
be used with granted communication actions such as
`context.Platform.Communication.SendDirectMessageAsync(...)`.
Create deferred sequences with `AddPersonalTodoItemRequest.StartInBacklog`; activate only the next
authorized item through `PlatformPersonalTodoClient.ActivateAsync`.

## Manifest decisions

- `provides` describes work this agent performs. Custom names are allowed and become provider
  capabilities after installation and binding.
- `requires` requests platform or provider authority. A grant and, for provider capabilities, a
  same-organization binding are still required at runtime.
- Use `work-item` when repeating the same durable work item must return the same logical result.
  Use `caller-key` only when the input schema contains a stable caller-supplied idempotency key.
  Use `none` only for genuinely read-only or repeat-safe work.
- Use exact object JSON Schemas with `additionalProperties: false` for stable typed contracts.
- Set the lowest practical timeout, between 1 and 900 seconds.
- Do not declare `events.publishes`; business effects use explicit capabilities.
- Do not add network rules unless the agent truly needs an external origin. All access remains
  brokered and installation-approved.

## Completion checklist

- IDs and versions match in code, project documentation, tests, and manifest.
- The manifest is at the repository root and its `runtime.projectPath` resolves to the executable
  project.
- Every provided capability has description, input/output schemas, timeout, and idempotency.
- Every requested grant/event/network rule is necessary and tested.
- Callback cancellation is honored; malformed and unsupported work fails safely.
- No raw transport, token, credential, database, Docker, or unrestricted networking code exists.
- `dotnet test` succeeds without C-Sweet credentials or a running C-Sweet instance.

Human-oriented explanations and examples are in
[`docs/creating-an-agent.md`](docs/creating-an-agent.md).
