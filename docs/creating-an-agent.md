# Creating a C-Sweet agent

This guide describes the minimum reliable shape of a third-party C-Sweet agent. An agent is an
untrusted workload that connects to the C-Sweet broker, receives events, and invokes explicitly
granted platform capabilities. It never receives database credentials or direct access to another
organization's state.

## 1. Create the host

Create a .NET executable, reference `CSweet.Agent.SDK`, derive the agent from `CSweetAgentBase`,
and register it with the host:

```csharp
using CSweet.Agent.SDK;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);
builder.AddCSweetAgent<MyAgent>();
await builder.Build().RunAsync();
```

The implementation identity and version must exactly match the root `csweet-plugin.json` file.
The SDK rejects a mismatch during startup.

```csharp
public sealed class MyAgent : CSweetAgentBase
{
    public override string AgentId => "com.example.project-manager";
    public override string Version => "1.0.0";

    public override Task HandleEventAsync(
        DeliveredEvent message,
        AgentRuntimeContext context,
        CancellationToken cancellationToken)
    {
        // Route only event types this agent understands.
        return Task.CompletedTask;
    }
}
```

## 2. Declare the manifest

The manifest is a request for authority, not authority itself. Installation grants determine what
the broker will actually allow. Request the smallest set of capabilities and events the agent needs.

```json
{
  "manifestVersion": "1.0",
  "kind": "agent",
  "id": "com.example.project-manager",
  "name": "Example Project Manager",
  "version": "1.0.0",
  "publisher": { "id": "com.example", "name": "Example" },
  "runtime": {
    "type": "dotnet-project",
    "projectPath": "src/Example.ProjectManager/Example.ProjectManager.csproj",
    "targetFramework": "net10.0",
    "defaultActivationMode": "AlwaysOn",
    "supportsMultipleInstallations": true,
    "maximumConcurrentJobs": 1
  },
  "protocol": { "minimumVersion": "1.0", "maximumVersion": "1.x" },
  "provides": [],
  "requires": [
    {
      "name": "communication.message.send.v1",
      "scope": "organization",
      "purpose": "Send messages in conversations that contain this agent employee"
    }
  ],
  "events": {
    "subscribes": ["com.csweet.agent.onboarded.v1"],
    "publishes": []
  },
  "configuration": [],
  "credentials": [],
  "webAccess": { "mode": "None", "rules": [] },
  "ui": []
}
```

Set `supportsMultipleInstallations` only when separate installations can safely maintain separate
identity, configuration, state, and conversation context.

## 3. Handle employee onboarding

After C-Sweet creates the agent employee, its protected direct conversation, participants, grants,
and remaining business setup, the platform sends the exact installation a durable
`com.csweet.agent.onboarded.v1` event. The platform does not write an introductory message. The
agent decides whether to send a message, create a proposal, load context, or perform another
job-specific action.

The JSON payload is:

```csharp
public sealed record AgentOnboardedEvent(
    Guid OrganizationId,
    Guid AgentOrganizationUserId,
    Guid HiringOrganizationUserId,
    Guid ConversationId,
    DateTimeOffset OccurredAt);
```

The broker envelope's `EventId` is stable across retries. Treat it as an idempotency key. Validate
that the payload organization matches `AgentRuntimeContext.BusinessId`, and never accept tenant or
installation identity from arbitrary nested payload data without checking it against broker context.

### Required acknowledgement

The agent must invoke `agent.onboarding.complete.v1` only after all onboarding behavior has
succeeded. Returning from `HandleEventAsync` does not acknowledge the event. The acknowledgement
payload is:

```json
{ "eventId": "the-guid-from-DeliveredEvent.EventId" }
```

This lifecycle-control capability is validated against the connected installation and the targeted
event. It is not a general business-data grant. An installation cannot acknowledge another agent's
event.

A typical handler sends an agent-authored first message and then acknowledges:

```csharp
private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

private static async Task HandleOnboardedAsync(
    DeliveredEvent message,
    AgentRuntimeContext context,
    CancellationToken cancellationToken)
{
    var input = JsonSerializer.Deserialize<AgentOnboardedEvent>(message.Payload.Span, JsonOptions)
        ?? throw new InvalidOperationException("Onboarding payload is empty.");
    if (!Guid.TryParse(message.EventId, out var eventId) ||
        !string.Equals(context.BusinessId, input.OrganizationId.ToString("D"),
            StringComparison.OrdinalIgnoreCase))
        throw new InvalidOperationException("Onboarding identity is invalid.");

    var send = await context.Broker.InvokeCapabilityAsync(new RequestCapability
    {
        RequestId = Guid.NewGuid().ToString("N"),
        Capability = "communication.message.send.v1",
        ContentType = "application/json",
        Payload = ByteString.CopyFrom(JsonSerializer.SerializeToUtf8Bytes(new
        {
            chatId = input.ConversationId,
            content = "Thanks for hiring me. What outcome should I focus on first?",
            idempotencyKey = $"agent-onboarded:{message.EventId}"
        }, JsonOptions))
    }, message.EventId, cancellationToken);
    if (!send.Succeeded)
        throw new InvalidOperationException($"Could not send onboarding message: {send.Error}");

    var acknowledgement = await context.Broker.InvokeCapabilityAsync(new RequestCapability
    {
        RequestId = Guid.NewGuid().ToString("N"),
        Capability = "agent.onboarding.complete.v1",
        ContentType = "application/json",
        Payload = ByteString.CopyFrom(JsonSerializer.SerializeToUtf8Bytes(
            new { eventId }, JsonOptions))
    }, message.EventId, cancellationToken);
    if (!acknowledgement.Succeeded)
        throw new InvalidOperationException($"Could not acknowledge onboarding: {acknowledgement.Error}");
}
```

Add the necessary imports for `CSweet.Agent.Contracts.Grpc`, `Google.Protobuf`, and
`System.Text.Json`.

## 4. Design for duplicates and failure

Delivery is at least once. The platform retries the same stable onboarding event when the agent is
offline, throws, sends a message but crashes before acknowledgement, or simply forgets to
acknowledge. Every side effect performed before acknowledgement must therefore be idempotent.

For chat messages, supply an `idempotencyKey` derived from the lifecycle event ID. For proposals,
workstreams, external calls, or agent-owned storage, use the same event ID in that subsystem's
idempotency boundary. Do not acknowledge first and perform work afterward: a crash would permanently
lose the work.

C-Sweet intentionally stops retrying after a bounded number of attempts. The operator-configurable
default is 12 attempts. When exhausted, the lifecycle record is marked failed and the hiring user
receives a real-time notification containing the agent employee, installation, lifecycle event, and
last known failure. Correct agent code promptly; do not depend on infinite platform retries.

## 5. Testing checklist

Before publishing an agent, test all of the following:

- manifest ID and version match the implementation;
- the onboarding event is routed by exact event type;
- payload organization identity is checked against broker context;
- the agent performs its chosen behavior before acknowledgement;
- every side effect uses the stable event ID for idempotency;
- receiving the same event twice creates no duplicate messages or records;
- a failed side effect does not send the acknowledgement;
- a failed acknowledgement can be retried safely;
- required capabilities are present in the manifest and approved installation grant;
- malformed or cross-organization payloads are rejected without side effects;
- cancellation is propagated to broker calls;
- logs include event and conversation identifiers but no secrets or private prompt contents.

## 6. Operational rules

Treat broker denials, unavailable services, cancellation, duplicate delivery, and grant revocation as
normal conditions. Never loop forever inside an event handler. Let the platform's bounded delivery
policy retry durable work. Use structured logs and return safe errors without exposing credentials,
hidden prompts, or data from another organization.
