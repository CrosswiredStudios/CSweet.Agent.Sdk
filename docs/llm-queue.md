# LLM queue and waiting budgets (SDK 3.35.0)

During a leased agent callback, `context.CreateChatClient(...)` automatically uses the host's
authenticated inference job protocol when advertised. Agents keep using the same public chat
client. No new capability grants, polling code, or credentials belong in agent implementations.

The host acknowledges receipt and queues requests FIFO per provider profile. The default is one
concurrent provider request, with 256 queued requests. Conversation activity shows receipt,
waiting for a provider slot, processing, and completion. Host run diagnostics retain queue state.
These are C-Sweet acknowledgements: the provider's internal queue is not observable through the
standard completion API. "Processing" means C-Sweet has dispatched the request, not that a
provider has positively confirmed GPU execution.

Each short status request is authenticated against the owning runtime and exact work attempt.
The host verifies its live lease and grant before extending the work/runtime deadlines for time
spent waiting on inference. The SDK applies the host deadline only to that async callback, and
the chat worker follows the same durable work deadline. Queue and provider waiting therefore do
not consume the agent's execution budget. Generation has a separate 900-second limit, starting
after admission; queue waiting does not consume that limit. Original cancellation and lease
revocation remain active. An expired lease is never revived. Abandoned jobs are cancelled after
60 seconds without polling, and completed buffered results are retained for five minutes.

The Office broker buffers responses, so this protocol uses short start/read/cancel calls instead
of holding one HTTP/SSE operation open throughout the wait. LLM generation remains streamed into
a bounded host buffer and paged back to the SDK. A host restart interrupts those in-memory jobs;
the agent's existing durable work failure/retry path applies. Results are runtime-private and
cannot be read by another agent. Prompts and credentials are not added to queue diagnostics.

Host configuration:

```json
{
  "CSweet": {
    "Llm": {
      "Queue": {
        "MaximumConcurrentRequests": 1,
        "MaximumQueuedRequests": 256,
        "GenerationTimeoutSeconds": 900
      }
    },
    "AgentRuntime": { "InferenceWaitAllowanceSeconds": 86400 }
  }
}
```

The runtime launcher reserves up to 24 hours of extra VM/broker wall-clock lifetime for
inference-capable agents. This is an infrastructure safety ceiling, not extra agent execution
time. Only host-verified inference waits extend the original active budget. Configure the
allowance consistently in AgentHost and the runtime launcher, and restart existing workloads
to apply a new VM lifetime. Cancellation, generation timeout, and that infrastructure ceiling
remain effective.

The queue coordinator is scoped to one AgentHost process, matching the current local deployment.
Multiple AgentHost replicas require sticky routing and a shared admission coordinator before
claiming a provider-wide concurrency limit. Separate API-side fallback chat calls and external
provider clients do not use this agent queue. Older agents share AgentHost admission limits but
retain their old transport timeout; rebuild/reimport them with SDK 3.35.0 to enable polling.
Older hosts retain the previous SDK streaming path through capability negotiation.

Release checks cover per-provider serialization, queued cancellation, replay keys, runtime
ownership, refusal to revive expired work, SDK deadline propagation and explicit cancellation.
