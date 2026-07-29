# Manifest v2 reference

Every executable agent repository has exactly one `csweet-plugin.json` at its root. The
machine-readable definition is [`schemas/csweet-plugin.v2.schema.json`](../schemas/csweet-plugin.v2.schema.json).

## Complete safe baseline

```json
{
  "$schema": "https://raw.githubusercontent.com/CrosswiredStudios/CSweetAgentSdk/main/schemas/csweet-plugin.v2.schema.json",
  "manifestVersion": "2.0",
  "kind": "agent",
  "id": "com.example.research-agent",
  "name": "Research Agent",
  "version": "0.1.0",
  "publisher": { "id": "com.example", "name": "Example" },
  "runtime": {
    "type": "dotnet-project",
    "projectPath": "src/ResearchAgent/ResearchAgent.csproj",
    "targetFramework": "net10.0",
    "defaultActivationMode": "Manual",
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
    "roleAliases": ["researcher"],
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
| `runtime.defaultActivationMode` | `Manual`, `Periodic`, or `AlwaysOn`; prefer `Manual` |
| `runtime.supportsMultipleInstallations` | Whether separate installations can run independently |
| `runtime.maximumConcurrentJobs` | At least 1; start at 1 until concurrency safety is proven |
| `protocol` | `minimumVersion` `2.0`, `maximumVersion` beginning with `2.` |
| `provides` | Capability descriptors implemented by this package |
| `requires` | Minimum platform/provider authority requested from the installer |
| `events.subscribes` | Durable event names consumed by the package |
| `configuration` | Installation fields; configurable agents must provide describe/update |
| `credentials` | Named brokered credential bindings, never secret values |
| `webAccess` | `None`, `Allowlist`, or `AllPublic` brokered network policy |
| `ui` | Optional forms or views backed by capabilities in `provides` |
| `catalog` | Optional discovery summary, category, aliases, keywords, and documentation |

## Capability descriptors

Each `provides` entry requires a namespaced name, description, object input/output schemas, timeout
from 1–900 seconds, and one idempotency mode:

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
