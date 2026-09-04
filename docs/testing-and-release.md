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
- state-only interaction routing separately from action requests;
- empty, invalid, and budget-exhausted model responses;
- stage-local retries that do not repeat completed model calls or mutations;
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

## Installed-runtime acceptance

Before treating an agent as production-ready, run one acceptance pass through the same installation
path users will run. This is the boundary where source-only tests cannot prove effective grants,
resource scope, model/provider behavior, packaging, or deployed version selection.

Record evidence that:

1. C-Sweet imported the intended immutable revision and the active installation reports the expected
   agent and SDK versions.
2. Required configuration selects an approved provider and model without exposing credentials.
3. Each requested capability is both approved and effective for one representative authorized
   target; a representative out-of-scope target is denied safely.
4. A state-only onboarding or preference turn persists its choice without starting unrelated work.
5. A representative model call produces non-empty, structurally complete final content within the
   configured timeout and token budget.
6. One real downstream artifact, work, communication, or provider mutation succeeds and a replay
   with the same domain idempotency key does not duplicate it.
7. Injecting or simulating a downstream denial/failure preserves completed earlier stages and does
   not repeat model generation.
8. User-visible progress, completion, and blocked messages accurately match durable platform state.

Automate this pass where the deployment environment supports isolated fixtures. Otherwise keep a
short, repeatable manual smoke script with the release evidence. A successful local model request is
useful diagnostics but is not a substitute for the brokered installed path.

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
- Model reasoning, temperature, output limits, validation, and retry budgets are intentional for
  each operation.
- The installed-runtime acceptance pass used the exact revision and configured provider/model.
- Logs, progress, results, and errors contain no secrets or sensitive prompts.
- External mutations use stable domain idempotency keys.
- Retried downstream failures resume their stage without repeating completed model generation.
- README explains purpose, configuration, provided work, required authority, side effects, and
  limitations.
