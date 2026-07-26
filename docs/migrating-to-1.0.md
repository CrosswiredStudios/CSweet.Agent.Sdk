# Migrating to SDK 1.0

SDK 1.0 is a breaking security release. There is no executable protocol-v1 compatibility mode.

| Before 1.0 | SDK 1.0 |
|---|---|
| protobuf `CapabilityRequest` | `AgentCapabilityRequest` |
| protobuf delivered event | `AgentEventEnvelope` |
| `AgentCapabilityExecutionResult` | `AgentWorkResult` |
| `context.Broker.InvokeCapabilityAsync` | typed `context.Platform` call |
| `context.Broker.PublishEventAsync` chunk | `context.ReportProgressAsync` |
| generic publication for business effect | explicit platform capability |
| `BrokerLlmClient` | `context.CreateChatClient` / `PlatformChatClient` |
| `PlatformToolAdapters` | `context.GetModelToolsAsync` |
| `IAgentBrokerClient` fake | `AgentTestRuntime` |
| target installation in a request | install-time platform binding |

Change event payload reads from transport bytes to `message.Data` and deserialize from `JsonElement`. Return `AgentWorkResult.Success(value)` or `AgentWorkResult.Failure(message)`. Remove protobuf, gRPC, manual registration, transport URLs, tokens, channels, reconnect loops, and lease logic.

Update the package to `CSweet.Agent.SDK` 1.0 and keep `builder.AddCSweetAgent<TAgent>()`. Change the manifest to v2/protocol 2.x. Add schemas, timeout, and idempotency to every provided capability; remove `events.publishes`; list baseline authority such as `platform.user-input.request.v1` in `requires`.

Streaming response chunks are progress. The terminal response is work completion. Business mutations are explicit tool calls. Agent-to-agent calls use the capability name only; C-Sweet resolves the approved provider binding.

Replace transport mocks with `AgentTestRuntime.RegisterCapability`. Assert callback results and `runtime.Progress`. Add tests proving an unregistered capability is denied and cancellation is honored.

