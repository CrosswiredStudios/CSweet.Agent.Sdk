# Typed compute client

SDK 3.41.0 exposes `context.Platform.Compute`. This calls the same authorized broker as the platform's MCP compute tools; agent authors do not implement MCP, provisioning, UAC or credential handling.

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
