# Typed compute client

SDK 3.44.2 exposes `context.Platform.Compute`. This calls the same authorized broker as the platform's MCP compute tools; agent authors do not implement MCP, provisioning, UAC or credential handling.

Use `CSweet.Agent.SDK.Compute` for typed requests, results, `ComputeCapabilities` and `ComputeEvents`. Declare only the capabilities your agent uses in its manifest. Declarations do not grant permission. Provisioning, lifecycle changes, command execution and network publication remain separately authorized.

```csharp
using CSweet.Agent.SDK.Compute;

var environment = await context.Platform.Compute.ProvisionAsync(new(
    workstreamId, "hello-world", "hello-world:provision",
    new ComputeSpecification("linux", "x64", templateId,
        new ComputeResources(1, 1024, 20480), 3600)), cancellationToken);
```

Workstream and template selection are platform configuration, not technical details to request from the user. Linux is explicit here; Windows and other approved template identifiers use the same API. No Docker installation, network connection or public endpoint is implied.

Provisioning returns current state, not a ready-machine guarantee. Subscribe to `ComputeEvents.Changed` (`com.csweet.compute.changed.v1`), deserialize `ComputeChangedEvent`, then call `ReadAsync(change.EnvironmentId)`. Events may be duplicated, delayed or missed. Use durable task state and bounded reconnect recovery, not agent polling loops. `ListAsync(workstreamId, afterId, limit)` provides bounded discovery and returns `NextAfterId`.

Once ready, `ExecuteAsync(ExecuteComputeCommandRequest)` queues one guest command and returns a `ComputeOperation`. Call `ReadOperationAsync(operation.Id)` for its result after a wake. The current platform permits 30 seconds and 8192 output bytes per command. Executable and working directory are absolute guest paths. Arguments remain literal; the SDK does not construct a shell command. Long-running services must be started deliberately inside the guest. Output is available as exact bytes and UTF-8 text accessors.

`PublishPortAsync(PublishComputePortRequest)` is a separate operation requiring both inbound and publish-port grants. Its completed result supplies the health-checked URL and expiry. Current Hyper-V previews are reachable only on the provider machine. The SDK does not synthesize a URL or turn provisioning into network authority.

`StartAsync`, `StopAsync`, `RestartAsync` and `DestroyAsync` take `ComputeLifecycleRequest`. All mutation requests require stable idempotency keys and the expected generation where applicable. Persist those values across callbacks and restarts. Do not replace an uncertain command's key or automatically re-execute it. An operation status of `Completed` still requires checking its result, exit code, timeout, and error fields; `outcome-unknown` is not success.

No SDK method installs host components, enrolls a provider, supplies a host image path, changes grants, or chooses a caller installation identity. The application owns setup and any UAC prompt. SDK validation catches malformed inputs; the broker remains authoritative for current scope, policy, resource bounds and guest-OS validation.

Executable examples and compatibility are covered by `PlatformComputeClientTests`, Core's `ComputeSdkContractTests`, and Software Developer's Hello World workflow.

Call `context.Platform.Compute.GetDefaultsAsync()` to read the application's selected workstream and Linux template. `Pending` or `Running` means preparation continues; retain the task durably and re-read after `ComputeEvents.Available`. `Ready` supplies `WorkstreamId` and `TemplateId` for `ProvisionAsync`. Availability events are wake hints, not execution grants. The application owns setup, enrollment and administrator elevation. Never ask the user to enter IDs or run a script. Callers can still supply an explicitly approved workstream/template for advanced use.
## Personal development workspaces (3.44.2)

Inside a claimed personal-ticket callback, call `context.Platform.Git.PreparePersonalAsync(new(item.Id, stableKey), token)`.
Request `source-control.personal-work.prepare.v1` separately in the manifest. Core checks current installation approval,
ticket ownership, its retained conversation source, live claim, business repository policy and quota. It selects a private
C-Sweet repository and branch; the agent cannot select another repository or obtain credentials. Use the returned workspace
with the existing Git inspect/publish SDK methods. Personal queue completion and deferral remain SDK-owned.

Compute defaults grant no network access. Local test publication needs explicit inbound and publish-port grants for the
instance and guest port. Outbound access, private networking and public exposure are separate authorities; declaring a
capability does not grant it. The current Hyper-V provider keeps VMs without network adapters and fails unsupported network
requests closed. Docker inside a prepared guest image does not add host or outbound authority.

## Isolated source snapshots (SDK 3.44.2)

`git.workspace.sync.v1` transfers an authorized workspace snapshot through Core. `context.Platform.Git.MaterializeAsync` creates a runtime-local writable copy and returns its local path; the platform path from Prepare is an opaque workspace location, not a shared filesystem mount. `UploadAsync` sends edited files back before inspection/publication. Existing edits are preserved on repeated materialization; after runtime loss, the latest uploaded snapshot is restored.

Transfer requires the sync declaration plus existing preparation/publication authority for the exact assignment. No repository coordinates or credentials are accepted. Limits are 512 KiB compressed per snapshot, 16 MiB content and 4,096 files. Git metadata, redirected paths and traversal are rejected; local `.csweet` control files are excluded from uploads. An uploaded snapshot does not itself publish a commit.
