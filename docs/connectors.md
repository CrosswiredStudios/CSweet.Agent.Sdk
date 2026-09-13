# Protocol 2.1/2.2/2.3 connector contracts (SDK 3.41.0)

## Conditional changes (protocol 2.3)

An operation may declare `http.ifMatchInput`, a JSON pointer to a required, bounded string input,
for an exact strong entity tag obtained from an authenticated provider read. This is limited to
non-bootstrap, non-media PUT/PATCH/DELETE mutations. The host freezes the value with the request
and sends `If-Match` only on that exact approved mutation; there is no arbitrary header surface.
Use `ConnectorEntityTag.RequireStrong` before requesting an action. A stale version returns a blocked
action with condition `resource_changed`; obtain fresh evidence and a new approval, never silently
replace the version or resubmit. Timeouts still require reconciliation. Minimum protocol 2.3 ensures
older hosts reject this contract instead of sending an unconditional change.

## Response ownership (protocol 2.2)

For endpoints whose account is implicit in authorization, declare `http.responseResourcePointers`,
for example `["/items/*/ownerId"]`. Every selected string must exactly match the connection's confirmed
resource before the host releases or persists provider data, including mutation/media results. Pointers
are bounded to 256 characters, eight unique entries, and one array wildcard each. An existing empty
array is allowed; missing paths, non-array wildcard targets, non-string owners and mixed owners fail
closed. This is a response check, not a substitute for pre-mutation ownership validation.

These declarations require `protocol.minimumVersion: "2.2"` and cannot be used during bootstrap.
Earlier hosts reject that protocol rather than silently ignoring the security declaration. The host
freezes the declarations into the reviewed request plan; neither runtime supplies the expected owner.

## Typed action lifecycle

For media actions, take an attachment from authoritative `ReadChatAsync` history and require its
non-null `MediaAssetId`. Keep the exact conversation, message and attachment IDs as a
`ConversationAttachmentReference`; set it on `RequestConnectorAction.MediaSource` while the typed
provider input carries the asset ID. Never infer asset IDs from file names or treat attachment IDs as
asset IDs. The host validates and freezes their relationship, and rechecks live access during transfer.
This is metadata provenance, not a credential or standing approval. If the host lacks asset metadata
or source enforcement, guide the user to an updated host rather than falling back to raw transfers.

Media mappings may declare `http.mediaProtocol: "resumable-range.v1"` together with
`mediaInput`. This is a fixed host protocol, not custom code or arbitrary HTTP. The
host freezes the protocol with the media digest and exact request. It keeps the
provider session URL opaque, validates its destination, and queries server progress
before recovering an interrupted chunk. Missing or unsupported protocols must not
fall back to caller-directed transfer. This declaration does not establish that a
host has implemented durable media execution; unsupported transfers must fail closed.

Use `context.Platform.Connectors.RequestActionAsync(new RequestConnectorAction(capability, input,
idempotencyKey))` for a mutation and persist the returned `ConnectorAction.ActionId`. Provider-specific
packages should wrap this in narrow typed methods. Requesting a change does not execute it. The host
selects the explicitly bound account, freezes the plan and requests its exact approval. Read it with
`ReadActionAsync(new ReadConnectorAction(actionId))`; only `Completed` plus a persisted result proves
completion. `Approved` means permission has been recorded, not that provider work succeeded.

`ConnectorAction.Decision` carries the authoritative choice, bounded revision feedback and decision
time after the host revalidates result authority. Feedback is data for a new draft, not permission
to modify an approved plan. Cancel a pending action with
`CancelActionAsync(new CancelConnectorAction(actionId, stableCancellationKey))` and declare
`platform.connector.action.cancel.v1` separately. Only a `Cancelled` receipt confirms cancellation.
The host must atomically fence cancellation against execution; an executing, completed or uncertain
action cannot be cancelled away. Rejected/revision-requested/blocked actions may be closed, but the
original decision is retained in audit. Cancellation never rolls back a provider effect.

Declare both needed connector-action controls and the independent provider-operation capabilities.
Subscribe to `ConnectorActionEvents.Changed`, correlate by action ID and reread state on every wake.
Use durable personal work while waiting; do not keep an agent turn alive. Indeterminate outcomes
must reconcile or visibly block, never retry under a fresh key. Credentials, account selection,
approval authority, request destinations and upload URLs are not arguments to this API.

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
