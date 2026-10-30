# Manifest v2 reference

SDK 5.7.1 adds provider-neutral enterprise `accounts` management for protocol 2.4 connectors.
See [enterprise account contracts](connectors.md) for the native layout, bounded discovery,
permission groups and policy links. Account setup uses `setup.required: false` and an explicit
OAuth step in `accounts.setupFlow`; installation does not connect a user or grant business access.
Older hosts must reject unsupported account semantics.

SDK 3.35.0 adds protocol 2.1 [connector and dependency contracts](connectors.md).
These require explicit host enforcement; declarations are not authority.

Every executable agent repository has exactly one `csweet-plugin.json` at its root. The
machine-readable definition is [`schemas/csweet-plugin.v2.schema.json`](../schemas/csweet-plugin.v2.schema.json).

## Complete safe baseline

```json
{
  "$schema": "https://raw.githubusercontent.com/CrosswiredStudios/CSweetAgentSdk/main/schemas/csweet-plugin.v2.schema.json",
  "manifestVersion": "2.0",
  "kind": "agent",
  "rolePolicy": {
    "profile": "individual-contributor.v1",
    "declaredRoleKeys": ["researcher"],
    "specializationKeys": ["market-research"]
  },
  "workItemTypes": { "requires": [] },
  "id": "com.example.research-agent",
  "name": "Research Agent",
  "version": "0.1.0",
  "publisher": { "id": "com.example", "name": "Example" },
  "runtime": {
    "type": "dotnet-project",
    "projectPath": "src/ResearchAgent/ResearchAgent.csproj",
    "targetFramework": "net10.0",
    "defaultActivationMode": "OnDemand",
    "defaultTickFrequencySeconds": 300,
    "supportsMultipleInstallations": true,
    "maximumConcurrentJobs": 1
  },
  "protocol": { "minimumVersion": "2.0", "maximumVersion": "2.x" },
  "provides": [
    {
      "name": "research.answer.v1",
      "description": "Answer a bounded research question.",
      "inputSchema": {
        "type": "object",
        "properties": { "question": { "type": "string", "minLength": 1 } },
        "required": ["question"],
        "additionalProperties": false
      },
      "outputSchema": {
        "type": "object",
        "properties": { "answer": { "type": "string" } },
        "required": ["answer"],
        "additionalProperties": false
      },
      "executionTimeoutSeconds": 120,
      "idempotency": "work-item",
      "riskClass": "standard"
    }
  ],
  "requires": [],
  "events": { "subscribes": [] },
  "configuration": [],
  "credentials": [],
  "webAccess": { "mode": "None", "rules": [] },
  "ui": [],
  "catalog": {
    "summary": "Answers bounded research questions.",
    "category": "Research",
    "role": { "key": "researcher", "name": "Researcher" },
    "license": { "spdxId": "MIT" },
    "iconUrls": ["https://example.com/researcher.png"],
    "roleAliases": ["Research Analyst"],
    "keywords": ["research"],
    "documentationUrl": "README.md"
  }
}
```

## Fields

| Field | Requirements |
|---|---|
| `$schema` | Optional editor hint; use the checked-in v2 schema URL |
| `manifestVersion` | Exactly `2.0` |
| `kind` | `agent` or `service` |
| `id` | Stable 1–200 character identifier using letters, numbers, dots, `_`, or `-` |
| `name` | Human display name |
| `version` | Semantic version such as `1.2.3` |
| `publisher` | Stable `id` plus display `name` |
| `runtime.type` | `dotnet-project` |
| `runtime.projectPath` | Relative `.csproj` path with no parent traversal |
| `runtime.targetFramework` | .NET target framework; current authoring target is `net10.0` |
| `runtime.defaultActivationMode` | `AlwaysOn`, `OnDemand`, or `Scheduled`; prefer `OnDemand` for event-driven agents |
| `runtime.defaultTickFrequencySeconds` | Preferred platform-owned attention/run cadence, 60–86400 seconds; installation policy may impose a higher minimum |
| `runtime.supportsMultipleInstallations` | Whether separate installations can run independently |
| `runtime.maximumConcurrentJobs` | At least 1; start at 1 until concurrency safety is proven |
| `protocol` | `minimumVersion` `2.0`, `maximumVersion` beginning with `2.` |
| `workItemTypes.requires` | Stable work type keys that must have an available platform/provider definition before installation |
| `provides` | Capability descriptors implemented by this package |
| `requires` | Minimum platform/provider authority requested from the installer |
| `events.subscribes` | Durable event names consumed by the package |
| `configuration` | Control-plane validated settings schema; no running agent is required |
| `credentials` | Named brokered credential bindings, never secret values |
| `connections` | OAuth provider declarations with approved HTTPS origins and named progressive scope sets |
| `mcpServers` | Optional exact remote MCP endpoints and per-tool capability allowlists |
| `providerOperations` | Optional typed provider commands with fixed endpoints, credential binding, effect, and schemas |
| `fileTransferTargets` | Optional brokered file-transfer targets constrained by protocol, host suffix, port, root, and operation |
| `setup` | Optional required, resumable setup flow made only from platform-owned safe step kinds |
| `webAccess` | `None`, `Allowlist`, or `AllPublic` brokered network policy |
| `ui` | Optional forms or views backed by capabilities in `provides` |
| `catalog` | Required for agents; declares canonical role, SPDX license, optional HTTPS icon URLs, discovery summary, category, aliases, keywords, and documentation |

Agent `catalog.role.key` values are stable lowercase kebab-case identifiers used for exact hiring
matches. `catalog.role.name` is the generic job title and is independent from the branded top-level
`name`. `catalog.license.spdxId` accepts an SPDX identifier or expression. Up to four
`catalog.iconUrls` entries may be supplied, and each must be an absolute HTTPS URL.

Configuration fields may declare a scalar `defaultValue`. C-Sweet uses it to pre-populate new
installation and hiring forms. Required fields should declare a usable default whenever the value
is package-defined; provider and model fields remain organization-specific, and secret fields must
not embed credentials in the manifest.

## Capability descriptors

Each `provides` entry requires a namespaced name, description, object input/output schemas, timeout
from 1–86400 seconds, and one idempotency mode:

- `work-item`: repeating the same durable work item is the same logical operation.
- `caller-key`: the input schema carries a stable caller-provided domain key.
- `none`: the operation is read-only or intrinsically repeat-safe.

`riskClass` defaults to `standard`. `descriptorHash` is optional and, when supplied, must match the
canonical descriptor computed by C-Sweet.

Each `requires` entry has `name`, `scope` (normally `organization` or `user`), and a concrete
`purpose` shown during installation review. Custom provider capability names are valid; the
approved provider binding is resolved by C-Sweet.

## Network and credentials

`None` requires no rules. `Allowlist` requires one or more exact rules. `AllPublic` has no rules
and is high risk. HTTP rules use `http`/`https`; WebSocket rules use `wss` and `GET`. A rule names a
DNS host, path prefix, methods, purpose, and optional credential binding. The credential's
`allowedOrigins` must include the rule origin. Agents never receive credential values.

Generic `events.publishes` is invalid in protocol v2. Use work progress or an explicit platform
capability for business effects.

Remote provider declarations are authority ceilings, not grants. `mcpServers` binds a declared
OAuth connection to one HTTPS endpoint and names every callable remote tool. `providerOperations`
binds one stable capability to a fixed command and endpoint; it does not expose generic HTTP.
Effects are `read`, `write`, `fiscal-write`, or `security-sensitive-write`. Runtime platforms must
require an exact, unexpired, payload-hash-bound approval for every non-read effect.

`fileTransferTargets` supports restricted brokered transfer only. SFTP declarations require port
1–65535, a non-root relative path, at least one approved hostname suffix, and operations from
`probe`, `list`, `stat`, and `upload`. The declaration cannot authorize SSH, arbitrary commands,
path traversal, reusable credentials, or broader filesystem access.

## Connections and safe setup

`connections` declares public OAuth metadata only: a stable connection ID, an administrator- or
publisher-registered `providerProfile`, exact HTTPS origins, and named permission sets. Client
IDs, client secrets, access tokens, refresh tokens, authorization endpoints, and redirect URIs do
not belong in plugin manifests or configuration. A setup step may request only a scope set named
by its connection declaration.

`setup` is a resumable graph whose `entryFlow` references one declared flow. Steps are rendered by
C-Sweet and are limited to `permission-summary`, `oauth-connect`, `form`, `account-selector`,
`health-check`, `confirmation`, `permission-request`, and `disconnect`. Capability callbacks and
configuration keys must be declared in the same manifest. HTML, JavaScript, Razor, iframes,
remote UI, redirects, and executable expressions are not manifest features and fail validation.

Progressive permission sets should separate required read-only access from optional mutations.
Enabling an optional feature must be initiated by the user and creates a fresh platform-owned
authorization flow; plugins cannot silently add scopes.
Numeric configuration fields may declare `lessThanFieldKey`. Both fields must be numeric, the
referenced key must exist, and the dependent value must remain strictly lower. This is useful for
relationships such as model output tokens being lower than context-window tokens. New signed
defaults must also satisfy the relationship.

Any configuration field may declare `visibleWhenFieldKey` and `visibleWhenValue` together. C-Sweet
shows the field only when the referenced field's current string value exactly matches the declared
value. The referenced key must exist, select values must be declared options, and visibility
relationships cannot reference themselves or form cycles. A required conditional field is required
only while it is visible.
## Role policy and specialization

Every agent manifest declares a `rolePolicy`. `declaredRoleKeys` contains the stable, high-level role categories the agent can fill, such as `software-architect` or `software-developer`. These keys—not display names or job titles—control role eligibility.

`specializationKeys` contains strengths such as `game-development`, `distributed-systems`, or `realtime-3d`. A staffing preference ranks matching specializations higher without excluding other agents in the same core role. A work item may separately declare a genuinely required specialization; that explicit requirement remains an eligibility gate.

`RoleTaxonomy.CoreRoleKey` treats the domain labels `game-engineer` and `game-quality-assurance` as the `software-developer` and `software-qa` core roles. A candidate declaring either label in a family can fill that family's work when its required capabilities and any explicit required specializations match. The job title and preferred specializations preserve the game-specific intent.

```json
"rolePolicy": {
  "profile": "individual-contributor.v1",
  "declaredRoleKeys": ["software-architect"],
  "specializationKeys": ["game-development", "distributed-systems"]
}
```

## Marketplace card branding (SDK 3.46.1)

Optional `catalog` keys customize discovery cards:

```json
{
  "summary": "Turns ideas into a prioritized product plan.",
  "longDescription": "Defines the product direction, organizes discovery, and delivers requirements and a roadmap your team can act on.",
  "imageUrl": "https://assets.example.com/agents/product-manager.webp",
  "companyLogoUrl": "https://assets.example.com/company-logo.svg",
  "accentColor": "#1C6252"
}
```

`imageUrl` is the agent portrait, illustration, or artwork; `companyLogoUrl` is the
publisher's logo. Use absolute HTTPS URLs of at most 2,048 characters, without user
credentials. `accentColor` accepts exactly six hexadecimal digits prefixed with `#`.
`longDescription` is plain text, up to 2,000 characters, shown in the hover/tap panel
and profile. It does not replace `summary` on the front of the card.

All four values can be omitted or null. Blank values also use defaults. Missing or
failed artwork uses C-Sweet's bundled agent image. Missing or failed company logos
show `publisher.name`; no separate company-name field is required. An omitted accent
uses the default marketplace accent, and an omitted long description uses `summary`.
Legacy `iconUrls` remain independent icons; they are not assumed to be portraits or
publisher logos. Artwork never controls availability, trust, or permissions.

## Project requirement

`rolePolicy.requiresProject` is an optional boolean, defaulting to `false` for compatibility.
Set it to `true` for any role that needs an assigned project before delivery work, such as a
developer, artist, or designer. It is independent of `declaredRoleKeys` and grants no authority.
The platform checks the active Workstream, explicit participation, matching board/team, and
current grants at delivery boundaries and on execution recovery. Configuration, conversation,
and setup coordination remain available before a project exists.

Use `context.Platform.Projects` to retain a human request, discover eligible projects, record
an authenticated human choice, read its current state/setup URL, and resume agent-owned ticket
planning after readiness. These operations cannot create a project, assign membership, or approve
hiring. Subscribe to `ProjectIntakeCapabilities.Changed`; events are wake hints, so reread the intake.
Use `ListAsync` for bounded reconnect recovery. Chief/manager assistance operations require separate
grants and assignment checks. Creation and membership are authenticated human setup operations.


## Manager base type

Declare `rolePolicy.baseType: "manager"` with `profile: "manager.v1"` and derive from
`CSweetManagerAgentBase`. Product management and game production are specialized manager roles.
See [manager authoring](manager-agents.md) for shared monitoring, assessment, recovery hooks and required grants.

## Reviewed host remote MCP (5.8.0)

A reviewed remote MCP provider uses `kind: "remote-mcp"`, `runtime.type: "host-remote-mcp"`,
manifest version 2.0 and protocol minimum **2.6**. The trusted host holds the key and
calls the remote server; no plugin process exists.

```json
"reviewedRemoteMcp": {
  "profile": "acme.read.v1",
  "endpoint": "https://api.acme.example/mcp",
  "authentication": "api-key",
  "header": "X-Api-Key",
  "accountBinding": "owner-business-installation.v1",
  "transport": "stateless-post",
  "protocolVersions": ["2025-03-26"],
  "tools": [
    { "capability": "acme.orders.search.v1", "remoteTool": "search_orders", "effect": "read" }
  ]
}
```

| Field | Rule |
|---|---|
| `profile` | Lowercase dotted identifier ending in `.vN` |
| `endpoint` | `https`, port 443, public DNS host, a path; no IP, credentials, query or fragment. Shown to the human at install review |
| `authentication` / `header` | `api-key` with an `X-` header, or `bearer` with `Authorization` |
| `accountBinding` | `owner-business-installation.v1` |
| `transport` | `stateless-post` (one request, one bounded JSON or single-event SSE response) |
| `protocolVersions` | Unique values from the host-supported MCP set (`2025-03-26`) |
| `tools` | Exactly one mapping per `provides` entry; unique capability and remote tool names; `effect: "read"` |

Each `provides` entry uses `idempotency: "none"` and a timeout of at most 30 seconds.
Its schemas must stay inside the closed, bounded subset in `ClosedJsonSchema`:

- Allowed keywords: `type`, `properties`, `required`, `items`, `additionalProperties`, `maxLength`, `minLength`, `maxItems`, `minItems`, `minimum`, `maximum`, `enum`, `format` (`uuid` only) and `description`.
- Every schema declares exactly one non-null type (optionally plus `null`); every object sets `additionalProperties: false` and declares at most 64 properties.
- Every string has `maxLength` (at most 4096), `format: "uuid"` or an `enum`; every array has `maxItems` (at most 100); depth is at most 8.
- The input schema root is a non-nullable object.

The remote tool's result must be a single MCP `text` content item whose text is JSON
matching `outputSchema`; anything else is withheld. At dispatch the host also checks that
each mapped remote tool is advertised with the same input property names and types.
Unmapped remote tools are ignored and can never be called.

Remote-MCP manifests cannot declare executable code, configuration, credentials,
connections, provider operations, accounts, setup, a role policy, web access, events or
UI. Protocol minimums 2.5 and 2.6 are reserved for this kind. The retired 5.7.x closed
RealtyLeads profile (protocol 2.5) is rejected with an update message.

Account entry and explicit consumer grants belong to the native host. Agent
manifests depend on the provider and request its typed business capabilities;
they cannot contain keys, endpoint overrides or account selectors. A valid manifest
grants no authority. See [security](../SECURITY.md) for the account boundary.
