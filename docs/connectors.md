# Protocol 2.1 connector contracts (SDK 3.30.0)

Connector packages are deterministic integration providers, not employees. Declare
`kind: connector`, one organization account connection, protocol minimum `2.1`,
multiple installations, and closed `providerOperations[].http` mappings. They cannot
request model/platform tools, raw network access, direct credentials, filesystem
transfers, or transitive dependencies. The host, not the package, materializes HTTP.

An agent declares `dependencies` with a local ID, exact package and publisher IDs,
inclusive `minimumVersion`, and exclusive `maximumVersionExclusive`. Required
capabilities reference that local ID through `dependency`. These declarations are
requirements, never grants. Binding requires explicit account selection, reviewed
package digests, and grants for both producer and consumer. Ordinary automatic
provider selection must not select a connector account.

`connections[].provider` contains only public OAuth metadata: display name,
authorization/token/revocation endpoints and bounded authorization extension
parameters. Security parameters such as state, redirect URI, scopes and PKCE are
host-owned. A copied publisher or profile ID confers no trust. An administrator
must approve the immutable executable build, destinations, operations and vault
profile; Google client secrets or tokens must never be embedded in packages.

HTTP mappings contain fixed endpoints/methods, fixed query/body values, typed
input JSON pointers, scope-set requirements, optional resource ownership reads,
media asset references and secret response pointers. Tool and operation schemas
must match. Mappings are data, not expression languages. Changing the package,
account, grants, resources, input or media invalidates a previously approved plan.

The host must freeze, approve and verify every outgoing request, including resumed
upload chunks. Resource ownership requires authenticated provider evidence, not a
caller-supplied account ID. Secret extraction must precede any runtime response or
audit payload. Unknown mutation outcomes require reconciliation, not blind retries.

These contracts do not by themselves implement OAuth, approval, media transfer or
durable execution. Hosts that do not enforce protocol 2.1 must reject the package.
Legacy, unrelated 2.0 workloads retain their existing supported execution path.

Account-selector and health-check steps on a connector can declare `accountOptions`
with bounded JSON pointers for the items array, ID, name, optional handle and next-page
token. The same manifest-declared read-only bootstrap operation discovers accessible
accounts and independently checks the confirmed account. The host returns only native
account choices or health status; it must not execute a connector runtime, model,
media transfer or secret-extraction operation for this flow. Names come from provider
results, not browser-submitted labels. Incomplete account pagination must fail visibly.

`http.boundResourceQueryPrefix` is an optional bounded literal prefix for provider query
formats such as `account==<confirmed-id>`. The resource always comes from the connection;
it is not an input substitution or expression. Prefixes participate in the frozen plan.

## Conversation-only setup assistance

An agent may request `setup.assistance: { "profile": "conversation.v1" }` with
required setup and protocol minimum 2.1. This does not enable connector bootstrap
to reason. The host intersects approved grants with text-only LLM conversation,
reading/sending in the designated protected setup conversation, and the native
`UserActionWorkflows.PluginSetupOpen` action. No attachments, other conversations,
business-data tools, filesystem, model tools or external work are authorized.

The host creates a durable obligation and delivers `PluginSetupEvents.Requested`
with `PluginSetupRequestedEvent` to the exact installation. Initial delivery and
one reminder after 24 hours use separate stable event identities. Persist messages
with stable idempotency keys; delivery can repeat after a crash. Activation remains
a host decision after validation; an agent message cannot mark setup complete.
