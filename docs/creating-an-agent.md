# Creating a C-Sweet agent

Agent code implements callbacks; the SDK owns runtime transport, authentication, discovery, durable work, leases, retries, and shutdown.

```csharp
using CSweet.Agent.SDK;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);
builder.AddCSweetAgent<ExampleAgent>();
await builder.Build().RunAsync();

sealed class ExampleAgent : CSweetAgentBase
{
    public override string AgentId => "com.example.agent";
    public override string Version => "1.0.0";

    public override Task HandleEventAsync(
        AgentEventEnvelope message,
        AgentRuntimeContext context,
        CancellationToken cancellationToken) =>
        context.ReportProgressAsync(new { stage = "received", message.EventType }, cancellationToken);

    protected override Task<AgentWorkResult> ExecuteCapabilityCoreAsync(
        AgentCapabilityRequest request,
        AgentRuntimeContext context,
        CancellationToken cancellationToken) =>
        Task.FromResult(request.Capability == "example.echo.v1"
            ? AgentWorkResult.Success(new { value = request.Arguments })
            : AgentWorkResult.Failure("Unsupported capability."));
}
```

The root `csweet-plugin.json` must use `manifestVersion: "2.0"` and protocol `2.0` through `2.x`. Each `provides` entry declares a description, input/output JSON Schemas, bounded execution timeout, and idempotency behavior. `requires` requests authority; an installer must explicitly grant it. Do not declare generic event publications.

Use typed methods on `context.Platform` for normal platform calls. Use `context.GetModelToolsAsync()` to obtain only live, model-visible tools from the current grant revision. Use `context.CreateChatClient(selection)` for platform-governed model streaming. Never cache tools beyond the revision returned by the SDK.

Progress must be useful, bounded, and ordered; the SDK assigns monotonic sequence numbers. Honor the callback cancellation token. Make external side effects idempotent with the domain key supplied by the work item, because work is delivered at least once after a crash or expired lease.

Tests require no network credential:

```csharp
var runtime = new AgentTestRuntime()
    .RegisterCapability<object, object>(
        "platform.example.read.v1",
        (request, token) => Task.FromResult<object>(new { value = 42 }));

var result = await runtime.ExecuteCapabilityAsync(
    new ExampleAgent(),
    "example.echo.v1",
    new { text = "hello" });
```

Test supported/unsupported capabilities, malformed inputs, cancellation, duplicate idempotency keys, progress, every requested platform capability, and safe failures. The sample runs locally with:

```powershell
dotnet run --project samples/HelloAgent -- --self-test
```
