# Manifest v2 reference

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
## Role policy and specialization

Every agent manifest declares a `rolePolicy`. `declaredRoleKeys` contains the stable, high-level role categories the agent can fill, such as `software-architect` or `software-developer`. These keys—not display names or job titles—control role eligibility.

`specializationKeys` contains optional strengths such as `game-development`, `distributed-systems`, or `realtime-3d`. Specializations may improve catalog ranking for a staffing preference, but they never make an agent ineligible for a matching high-level role category.

```json
"rolePolicy": {
  "profile": "individual-contributor.v1",
  "declaredRoleKeys": ["software-architect"],
  "specializationKeys": ["game-development", "distributed-systems"]
}
```
