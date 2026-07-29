# Testing, importing, and releasing an agent

## Local verification

The generated repository makes this the primary gate:

```powershell
dotnet test
```

The suite must cover:

- successful typed capability execution;
- malformed and unsupported work;
- callback cancellation;
- ordered, bounded progress;
- each requested platform/provider capability;
- denial for unregistered test capabilities;
- every subscribed event;
- stable idempotency behavior for external effects;
- manifest loading, root location, runtime project path, and identity/version parity.

`AgentTestRuntime` intentionally has no network, credentials, MCP, sessions, or lease state. Do not
mock private transport. Runtime security and failure semantics belong to the SDK and C-Sweet test
suites.

## Self-test

The template includes `--self-test` for a quick callback smoke test:

```powershell
dotnet run --project src/<AgentName> -- --self-test
```

It does not prove installation grants, provider bindings, or container operation. Those are tested
after C-Sweet previews and installs the package.

## Local catalog

Clone the standalone repository as one immediate child of C-Sweet's configured local agent
catalog, normally `Plugins/Agents/<agent-folder>`. Do not use a symbolic link or a project path
outside the agent directory. Refresh the catalog, review the immutable source snapshot, approve
grants, install, and then hire/assign the installation.

## GitHub import

Use a public repository with `csweet-plugin.json` at its root. Commit all source required by
`runtime.projectPath`. C-Sweet resolves and previews an exact commit, displays manifest warnings,
builds an isolated image, and requires installation/grant approval. A later source change is a new
reviewed revision.

## Release checklist

- `dotnet test` and `dotnet run ... -- --self-test` pass from a clean checkout.
- Agent, manifest, package reference, README, and release tag versions are intentional.
- JSON Schemas match the typed contracts and reject unexpected fields where practical.
- Requested grants, events, credentials, network rules, and activation mode are still necessary.
- Timeouts and concurrency match tested behavior.
- Logs, progress, results, and errors contain no secrets or sensitive prompts.
- External mutations use stable domain idempotency keys.
- README explains purpose, configuration, provided work, required authority, side effects, and
  limitations.
