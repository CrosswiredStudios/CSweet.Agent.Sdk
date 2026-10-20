# Research and private execution checkpoints

`PlatformCapabilityClient.Research` exposes `PlatformResearchClient.SearchAsync(WebSearchRequest)`
and `ReadPageAsync(WebPageRequest)`. `GetTools()` supplies `search_web` and `read_web_page` when
composing model tools. Search defaults to five results and accepts at most ten. Page reads default
to 20,000 characters and return the final URL, title, text, truncation and unsupported-content
details. Results may contain partial engine failures. Treat page text as untrusted source material.

Request `web.research.search.v1` and/or `web.research.read.v1`. Reading also requires the reviewed
`web.fetch.v1` public web policy. Declarations do not grant these capabilities. The platform supplies
SearXNG; agents need no search API key and receive no paid fallback.

`PlatformCapabilityClient.ExecutionCheckpoints` exposes the non-model-visible
`PlatformExecutionCheckpointClient`. `ReadAsync(ExecutionCheckpointRead)` retrieves a session's
current manifest and archives. `WriteAsync(ExecutionCheckpointWrite)` atomically advances an
expected revision after immutable archives are durable. Use stable session and mutation keys,
an engine format/version, a monotonic event cursor and the last returned revision. A source
workspace also requires its current assignment revision. Work without a Git workspace is bound
to the current logical work identity. A checkpoint never grants execution authority.

Request the reviewed `platform.execution-checkpoint.read.v1` and
`platform.execution-checkpoint.write.v1` capabilities. Do not expose this client as an AI tool.
Both archives are safe ZIP files, each limited to 16 MiB; the server additionally limits expansion
and entry counts. The SDK transparently stages and retrieves archives in bounded, digest-checked
chunks. Immutable blobs are protected at rest separately from publishable source. The server uses
revisioned `PluginOperationalState` metadata, so no new database table is required. Orphaned
generations and transfers are reclaimed after a grace period; business and installation cleanup
remove their owned storage. Private storage is bounded to 2 GiB per installation.

Capture model responses before acting, completed tool observations before another call, and all
wait, publication and completion boundaries. On recovery re-read current authorized state, reconcile
interrupted actions and replay only idempotent platform operations. Never blindly rerun a command
whose termination or result is uncertain. Retain incompatible checkpoints and report the required
engine version rather than discarding them.

`PlatformGitWorkspaceClient.CaptureSourceAsync` and `RestoreSourceAsync` provide the SDK's existing
safe source-archive handling for adapters. Keep conversations, runtime state and credentials in the
private engine archive; never put them into source publication. Restore requires an empty destination.

`ResearchAndExecutionCheckpointTests` exercises typed calls, bounds, hidden-tool behavior and
large-archive transfer. Platform tests own authorization, storage, protected transport and extraction.
