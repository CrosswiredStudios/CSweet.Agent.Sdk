using System.Text.Json;
using System.Text.RegularExpressions;
using System.Security.Cryptography;
using CSweet.Agent.Contracts.Packaging;

namespace CSweet.Agent.SDK;

/// <summary>Loads and validates canonical <c>csweet-plugin.json</c> manifests.</summary>
public static class AgentManifestLoader
{
    private static readonly JsonSerializerOptions CanonicalSerializerOptions =
        new(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = false };

    private static readonly JsonSerializerOptions LegacySerializerOptions =
        new() { PropertyNameCaseInsensitive = true };

    private static readonly Regex IdentifierPattern =
        new("^[A-Za-z0-9](?:[A-Za-z0-9._-]{0,198}[A-Za-z0-9])?$", RegexOptions.CultureInvariant);

    private static readonly Regex SemanticVersionPattern =
        new("^\\d+\\.\\d+\\.\\d+(?:-[0-9A-Za-z.-]+)?(?:\\+[0-9A-Za-z.-]+)?$", RegexOptions.CultureInvariant);

    private static readonly Regex TargetFrameworkPattern =
        new("^net\\d+\\.\\d+(?:-[A-Za-z0-9.-]+)?$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly HashSet<string> SafeSetupKinds = new(StringComparer.Ordinal)
    {
        "permission-summary", "oauth-connect", "form", "account-selector", "health-check",
        "confirmation", "permission-request", "disconnect"
    };

    /// <summary>Loads a manifest relative to the application base directory unless the path is absolute.</summary>
    public static async Task<AgentManifest> LoadAsync(
        string manifestPath,
        CancellationToken cancellationToken)
    {
        var resolvedPath = Path.IsPathRooted(manifestPath)
            ? manifestPath
            : Path.Combine(AppContext.BaseDirectory, manifestPath);

        if (!File.Exists(resolvedPath))
            throw new FileNotFoundException($"Agent manifest was not found at '{resolvedPath}'.", resolvedPath);

        var json = await File.ReadAllTextAsync(resolvedPath, cancellationToken);
        using var document = JsonDocument.Parse(json);
        var canonical = document.RootElement.TryGetProperty("kind", out _);
        var manifest = JsonSerializer.Deserialize<AgentManifest>(
            json,
            canonical ? CanonicalSerializerOptions : LegacySerializerOptions)
            ?? throw new InvalidOperationException("Agent manifest could not be deserialized.");

        if (canonical)
            manifest = WithCompatibilityProjections(manifest);

        Validate(manifest);
        return manifest;
    }

    /// <summary>
    /// Validates the author-visible protocol-v2 manifest rules enforced by the C-Sweet importer.
    /// Custom provider capability names are valid and are not restricted to <see cref="CapabilityCatalog"/>.
    /// </summary>
    public static void Validate(AgentManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        var errors = new List<string>();
        var provides = manifest.Provides ?? [];
        var requires = manifest.Requires ?? [];
        var events = manifest.Events ?? new AgentEventManifest([]);

        RequiredIdentifier(manifest.Id, "id", errors);
        Required(manifest.Name, "name", errors);
        if (!SemanticVersionPattern.IsMatch(manifest.Version ?? string.Empty))
            errors.Add("Plugin manifest version must be a semantic version such as 1.2.3.");
        if (manifest.Kind is not ("agent" or "service"))
            errors.Add("Plugin manifest kind must be 'agent' or 'service'.");
        if (manifest.ManifestVersion != "2.0")
            errors.Add("Executable plugins must use manifestVersion 2.0.");

        if (manifest.Publisher is null)
            errors.Add("Agent manifest publisher is required.");
        else
        {
            RequiredIdentifier(manifest.Publisher.Id, "publisher.id", errors);
            Required(manifest.Publisher.Name, "publisher.name", errors);
        }

        ValidateRuntime(manifest.Runtime, errors);
        ValidateRolePolicy(manifest, errors);
        ValidateWorkItemTypes(manifest.WorkItemTypes, errors);
        if (manifest.Protocol is null ||
            manifest.Protocol.MinimumVersion != "2.0" ||
            !manifest.Protocol.MaximumVersion.StartsWith("2.", StringComparison.Ordinal))
        {
            errors.Add("Executable plugins must require MCP runtime protocol 2.0 through 2.x.");
        }

        ValidateNames(provides.Select(x => x.Name), "provides", errors);
        ValidateNames(requires.Select(x => x.Name), "requires", errors);
        ValidateNames(events.Subscribes ?? [], "events.subscribes", errors);
        if (events.Publishes?.Count > 0)
            errors.Add("events.publishes is not supported in protocol v2; use explicit capabilities or work progress.");

        foreach (var capability in provides)
        {
            if (string.IsNullOrWhiteSpace(capability.Description))
                errors.Add($"provides capability '{capability.Name}' requires a description.");
            if (capability.InputSchema.ValueKind != JsonValueKind.Object ||
                capability.OutputSchema.ValueKind != JsonValueKind.Object)
                errors.Add($"provides capability '{capability.Name}' requires object inputSchema and outputSchema values.");
            if (capability.ExecutionTimeoutSeconds is < 1 or > 86_400)
                errors.Add($"provides capability '{capability.Name}' executionTimeoutSeconds must be between 1 and 86400.");
            if (capability.Idempotency is not ("work-item" or "caller-key" or "none"))
                errors.Add($"provides capability '{capability.Name}' idempotency is unsupported.");
            if (!string.IsNullOrWhiteSpace(capability.DescriptorHash) &&
                !string.Equals(
                    capability.DescriptorHash,
                    DescriptorHash(capability),
                    StringComparison.OrdinalIgnoreCase))
                errors.Add($"provides capability '{capability.Name}' descriptorHash does not match its canonical descriptor.");
        }

        ValidateConfiguration(manifest, errors);
        ValidateConnectionsAndSetup(manifest, errors);
        ValidateWebAccess(manifest, errors);

        if (errors.Count > 0)
            throw new InvalidOperationException(string.Join(" ", errors));
    }

    private static AgentManifest WithCompatibilityProjections(AgentManifest manifest) => new()
    {
        ManifestVersion = manifest.ManifestVersion,
        Kind = manifest.Kind,
        Id = manifest.Id,
        Name = manifest.Name,
        Version = manifest.Version,
        Publisher = manifest.Publisher,
        Runtime = manifest.Runtime,
        RolePolicy = manifest.RolePolicy,
        WorkItemTypes = manifest.WorkItemTypes ?? new AgentWorkItemTypesManifest(),
        Protocol = manifest.Protocol,
        Provides = manifest.Provides ?? [],
        Requires = manifest.Requires ?? [],
        Events = manifest.Events ?? new AgentEventManifest([]),
        Configuration = manifest.Configuration ?? [],
        Credentials = manifest.Credentials ?? [],
        Connections = manifest.Connections ?? [],
        Setup = manifest.Setup,
        WebAccess = manifest.WebAccess ?? new AgentWebAccessManifest(),
        Ui = manifest.Ui ?? [],
        Catalog = manifest.Catalog ?? new AgentCatalogMetadata(),
        Capabilities = (manifest.Provides ?? []).Select(x => x.Name).ToArray(),
        RequestedCapabilities = (manifest.Requires ?? []).Select(x => x.Name).ToArray(),
        RequestedSubscriptions = manifest.Events?.Subscribes ?? [],
        RequestedNetworkAccess = manifest.RequestedNetworkAccess
    };

    private static void ValidateWorkItemTypes(
        AgentWorkItemTypesManifest? declaration,
        ICollection<string> errors)
    {
        var required = declaration?.Requires ?? [];
        if (required.Count > 64 || required.Any(value =>
                string.IsNullOrWhiteSpace(value) || value.Length > 200 ||
                !IdentifierPattern.IsMatch(value)) ||
            required.Distinct(StringComparer.Ordinal).Count() != required.Count)
        {
            errors.Add("workItemTypes.requires must contain up to 64 unique stable type keys.");
        }
    }

    private static void ValidateRolePolicy(AgentManifest manifest, ICollection<string> errors)
    {
        if (manifest.RolePolicy is null)
            return;
        if (!AgentRolePolicyProfiles.All.Contains(manifest.RolePolicy.Profile))
            errors.Add("rolePolicy.profile must name a supported platform policy profile.");
        ValidateRoleTokens(manifest.RolePolicy.DeclaredRoleKeys, "rolePolicy.declaredRoleKeys", required: true, errors);
        ValidateRoleTokens(manifest.RolePolicy.SpecializationKeys, "rolePolicy.specializationKeys", required: false, errors);
    }

    private static void ValidateRoleTokens(
        IReadOnlyList<string>? values,
        string field,
        bool required,
        ICollection<string> errors)
    {
        values ??= [];
        if ((required && values.Count == 0) || values.Count > 32 ||
            values.Any(value => string.IsNullOrWhiteSpace(value) || value.Length > 160 ||
                                !RoleTaxonomy.IsCanonicalKey(value)) ||
            values.Distinct(StringComparer.Ordinal).Count() != values.Count)
        {
            errors.Add($"{field} must contain {(required ? "one to 32" : "up to 32")} unique lowercase kebab-case keys of at most 160 characters.");
        }
    }

    private static void ValidateRuntime(AgentRuntimeManifest? runtime, ICollection<string> errors)
    {
        if (runtime is null)
        {
            errors.Add("Agent manifest runtime is required.");
            return;
        }

        if (!string.Equals(runtime.Type, "dotnet-project", StringComparison.OrdinalIgnoreCase))
            errors.Add("Executable .NET plugins must use runtime.type 'dotnet-project'.");

        var projectPath = runtime.ProjectPath ?? string.Empty;
        var segments = projectPath.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);
        if (string.IsNullOrWhiteSpace(projectPath) ||
            Path.IsPathRooted(projectPath) ||
            projectPath.StartsWith('/') ||
            projectPath.StartsWith('\\') ||
            segments.Contains("..", StringComparer.Ordinal) ||
            !projectPath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            errors.Add("runtime.projectPath must be a relative .csproj path without parent traversal.");

        if (!TargetFrameworkPattern.IsMatch(runtime.TargetFramework ?? string.Empty))
            errors.Add("runtime.targetFramework must be a .NET target framework such as net10.0.");
        if (runtime.DefaultActivationMode is not ("AlwaysOn" or "OnDemand" or "Scheduled"))
            errors.Add("runtime.defaultActivationMode must be AlwaysOn, OnDemand, or Scheduled.");
        if (runtime.DefaultTickFrequencySeconds is { } frequency &&
            frequency is < 60 or > 86_400)
            errors.Add("runtime.defaultTickFrequencySeconds must be between 60 and 86400.");
        if (runtime.MaximumConcurrentJobs < 1)
            errors.Add("runtime.maximumConcurrentJobs must be at least one.");
    }

    private static void ValidateConfiguration(AgentManifest manifest, ICollection<string> errors)
    {
        var provided = (manifest.Provides ?? []).Select(x => x.Name).ToHashSet(StringComparer.Ordinal);
        if (manifest.Kind == "agent" && (manifest.Configuration?.Count ?? 0) > 0)
        {
            if (!provided.Contains(AgentConfigurationCapabilities.Describe))
                errors.Add($"Configurable agents must provide '{AgentConfigurationCapabilities.Describe}'.");
            if (!provided.Contains(AgentConfigurationCapabilities.Update))
                errors.Add($"Configurable agents must provide '{AgentConfigurationCapabilities.Update}'.");
        }

        foreach (var contribution in (manifest.Ui ?? []).Where(x => !string.IsNullOrWhiteSpace(x.Capability)))
            if (!provided.Contains(contribution.Capability!))
                errors.Add($"UI contribution '{contribution.Id}' references capability '{contribution.Capability}' that is not declared in provides.");

        var configuration = manifest.Configuration ?? [];
        var configurationByKey = configuration
            .Where(x => !string.IsNullOrWhiteSpace(x.Key))
            .GroupBy(x => x.Key, StringComparer.Ordinal)
            .ToDictionary(x => x.Key, x => x.First(), StringComparer.Ordinal);
        foreach (var field in configuration.Where(x => !string.IsNullOrWhiteSpace(x.LessThanFieldKey)))
        {
            if (!configurationByKey.TryGetValue(field.LessThanFieldKey!, out var target))
            {
                errors.Add($"Configuration field '{field.Key}' references unknown lessThanFieldKey '{field.LessThanFieldKey}'.");
                continue;
            }
            if (!string.Equals(field.Type, "number", StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(target.Type, "number", StringComparison.OrdinalIgnoreCase))
                errors.Add($"Configuration lessThanFieldKey '{field.Key}' -> '{target.Key}' requires number fields.");
            if (field.DefaultValue is { } value && target.DefaultValue is { } targetValue &&
                value.ValueKind == JsonValueKind.Number && targetValue.ValueKind == JsonValueKind.Number &&
                value.TryGetDecimal(out var number) && targetValue.TryGetDecimal(out var limit) && number >= limit)
                errors.Add($"Configuration default '{field.Key}' must be less than '{target.Key}'.");
        }

        foreach (var field in configuration)
        {
            var hasVisibilityKey = !string.IsNullOrWhiteSpace(field.VisibleWhenFieldKey);
            var hasVisibilityValue = !string.IsNullOrWhiteSpace(field.VisibleWhenValue);
            if (hasVisibilityKey != hasVisibilityValue)
            {
                errors.Add($"Configuration field '{field.Key}' must declare visibleWhenFieldKey and visibleWhenValue together.");
                continue;
            }
            if (!hasVisibilityKey)
                continue;
            if (!configurationByKey.TryGetValue(field.VisibleWhenFieldKey!, out var controller))
            {
                errors.Add($"Configuration field '{field.Key}' references unknown visibility field '{field.VisibleWhenFieldKey}'.");
                continue;
            }
            if (string.Equals(field.Key, controller.Key, StringComparison.Ordinal))
            {
                errors.Add($"Configuration field '{field.Key}' cannot control its own visibility.");
                continue;
            }
            var controllerType = controller.Type.Trim().ToLowerInvariant();
            if (controllerType is not ("string" or "text" or "textarea" or "select" or
                "provider" or "llmprovider" or "model" or "llmmodel"))
                errors.Add($"Configuration field '{field.Key}' visibility controller '{controller.Key}' must contain text.");
            else if (controllerType == "select" && controller.Options is { Count: > 0 } &&
                     !controller.Options.Any(option => string.Equals(
                         option.Value, field.VisibleWhenValue, StringComparison.Ordinal)))
                errors.Add($"Configuration field '{field.Key}' visibility value is not declared by '{controller.Key}'.");
        }

        foreach (var field in configuration.Where(x => !string.IsNullOrWhiteSpace(x.Key)))
        {
            var visited = new HashSet<string>(StringComparer.Ordinal) { field.Key };
            var current = field;
            while (!string.IsNullOrWhiteSpace(current.VisibleWhenFieldKey) &&
                   configurationByKey.TryGetValue(current.VisibleWhenFieldKey, out var controller))
            {
                if (!visited.Add(current.VisibleWhenFieldKey))
                {
                    errors.Add($"Configuration visibility cycle includes field '{field.Key}'.");
                    break;
                }
                current = controller;
            }
        }
    }

    private static void ValidateConnectionsAndSetup(AgentManifest manifest, ICollection<string> errors)
    {
        var connections = manifest.Connections ?? [];
        var connectionIds = connections.Select(x => x.Id).ToArray();
        ValidateNames(connectionIds, "connections.id", errors);

        foreach (var connection in connections)
        {
            if (connection.Type != "oauth2")
                errors.Add($"Connection '{connection.Id}' type must be 'oauth2'.");
            RequiredIdentifier(connection.ProviderProfile, $"connections.{connection.Id}.providerProfile", errors);
            if (connection.AllowedOrigins.Count == 0)
                errors.Add($"Connection '{connection.Id}' must declare at least one allowed origin.");
            foreach (var origin in connection.AllowedOrigins)
            {
                if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps ||
                    uri.AbsolutePath != "/" || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
                    errors.Add($"Connection '{connection.Id}' allowed origin '{origin}' must be an HTTPS origin without path, query, or fragment.");
            }
            if (connection.SecretResponseFields.Any(x => string.IsNullOrWhiteSpace(x) || !x.StartsWith('/') || x.Contains("..", StringComparison.Ordinal)) ||
                connection.SecretResponseFields.Distinct(StringComparer.Ordinal).Count() != connection.SecretResponseFields.Count)
                errors.Add($"Connection '{connection.Id}' secretResponseFields must be unique JSON pointers.");

            var scopeSetIds = connection.ScopeSets.Select(x => x.Id).ToArray();
            ValidateNames(scopeSetIds, $"connections.{connection.Id}.scopeSets.id", errors);
            foreach (var scopeSet in connection.ScopeSets)
            {
                Required(scopeSet.Label, $"connections.{connection.Id}.scopeSets.{scopeSet.Id}.label", errors);
                Required(scopeSet.Purpose, $"connections.{connection.Id}.scopeSets.{scopeSet.Id}.purpose", errors);
                if (scopeSet.Scopes.Count == 0 || scopeSet.Scopes.Any(string.IsNullOrWhiteSpace) ||
                    scopeSet.Scopes.Distinct(StringComparer.Ordinal).Count() != scopeSet.Scopes.Count)
                    errors.Add($"Connection '{connection.Id}' scope set '{scopeSet.Id}' must contain unique, non-empty scopes.");
            }
        }

        if (manifest.Setup is null)
            return;

        var setup = manifest.Setup;
        var flows = setup.Flows ?? [];
        var flowIds = flows.Select(x => x.Id).ToArray();
        ValidateNames(flowIds, "setup.flows.id", errors);
        if (setup.Required && flows.Count == 0)
            errors.Add("Required setup must declare at least one flow.");
        if (!flowIds.Contains(setup.EntryFlow, StringComparer.Ordinal))
            errors.Add("setup.entryFlow must reference a declared setup flow.");

        var provided = (manifest.Provides ?? []).Select(x => x.Name).ToHashSet(StringComparer.Ordinal);
        var configuration = (manifest.Configuration ?? []).Select(x => x.Key).ToHashSet(StringComparer.Ordinal);
        foreach (var flow in flows)
        {
            Required(flow.Title, $"setup.flows.{flow.Id}.title", errors);
            var stepIds = flow.Steps.Select(x => x.Id).ToArray();
            ValidateNames(stepIds, $"setup.flows.{flow.Id}.steps.id", errors);
            if (flow.Steps.Count == 0)
                errors.Add($"Setup flow '{flow.Id}' must declare at least one step.");

            foreach (var step in flow.Steps)
            {
                if (!SafeSetupKinds.Contains(step.Kind))
                    errors.Add($"Setup step '{step.Id}' uses unsafe or unsupported kind '{step.Kind}'.");
                Required(step.Title, $"setup.flows.{flow.Id}.steps.{step.Id}.title", errors);
                if (step.Kind is "oauth-connect" or "permission-request")
                {
                    var connection = connections.FirstOrDefault(x => x.Id == step.Connection);
                    if (connection is null)
                        errors.Add($"Setup step '{step.Id}' references undeclared connection '{step.Connection}'.");
                    else if (string.IsNullOrWhiteSpace(step.ScopeSet) ||
                             !connection.ScopeSets.Any(x => x.Id == step.ScopeSet))
                        errors.Add($"Setup step '{step.Id}' references undeclared scope set '{step.ScopeSet}'.");
                }
                if (!string.IsNullOrWhiteSpace(step.Capability) && !provided.Contains(step.Capability))
                    errors.Add($"Setup step '{step.Id}' references capability '{step.Capability}' that is not declared in provides.");
                foreach (var key in step.ConfigurationKeys)
                    if (!configuration.Contains(key))
                        errors.Add($"Setup step '{step.Id}' references undeclared configuration key '{key}'.");
            }
        }

        foreach (var contribution in manifest.Ui ?? [])
            if (!string.IsNullOrWhiteSpace(contribution.Flow) && !flowIds.Contains(contribution.Flow, StringComparer.Ordinal))
                errors.Add($"UI contribution '{contribution.Id}' references undeclared setup flow '{contribution.Flow}'.");
    }

    private static string DescriptorHash(AgentProvidedCapability capability)
    {
        var canonical = JsonSerializer.SerializeToUtf8Bytes(new
        {
            capability.Name,
            capability.Description,
            inputSchema = capability.InputSchema,
            outputSchema = capability.OutputSchema,
            capability.ExecutionTimeoutSeconds,
            capability.Idempotency,
            capability.RiskClass
        }, CanonicalSerializerOptions);
        return Convert.ToHexString(SHA256.HashData(canonical)).ToLowerInvariant();
    }

    private static void ValidateWebAccess(AgentManifest manifest, ICollection<string> errors)
    {
        var webAccess = manifest.WebAccess ?? new AgentWebAccessManifest();
        var mode = webAccess.Mode;
        if (mode is not ("None" or "Allowlist" or "AllPublic"))
            errors.Add("webAccess.mode must be None, Allowlist, or AllPublic.");
        if (mode == "None" && webAccess.Rules.Count > 0)
            errors.Add("webAccess.rules must be empty when mode is None.");
        if (mode == "Allowlist" && webAccess.Rules.Count == 0)
            errors.Add("webAccess.rules is required when mode is Allowlist.");
        if (mode == "AllPublic" && webAccess.Rules.Count > 0)
            errors.Add("webAccess.rules must be empty when mode is AllPublic.");

        var credentials = (manifest.Credentials ?? [])
            .Where(x => !string.IsNullOrWhiteSpace(x.Name))
            .GroupBy(x => x.Name, StringComparer.Ordinal)
            .ToDictionary(x => x.Key, x => x.First(), StringComparer.Ordinal);
        if (credentials.Count != (manifest.Credentials?.Count ?? 0))
            errors.Add("credentials must have unique, non-empty names.");

        foreach (var rule in webAccess.Rules)
        {
            if (rule.Protocol is not ("http" or "websocket"))
                errors.Add("webAccess rule protocol must be http or websocket.");
            if (rule.Scheme is not ("http" or "https" or "wss"))
                errors.Add("webAccess rule scheme must be http, https, or wss.");
            if (rule.Protocol == "http" && rule.Scheme is not ("http" or "https"))
                errors.Add("HTTP webAccess rules must use http or https.");
            if (rule.Protocol == "websocket" &&
                (rule.Scheme != "wss" || rule.Methods.Count != 1 || rule.Methods[0] != "GET"))
                errors.Add("WebSocket webAccess rules must use wss and GET.");
            if (string.IsNullOrWhiteSpace(rule.Host) || Uri.CheckHostName(rule.Host) == UriHostNameType.Unknown)
                errors.Add("webAccess rule host must be a DNS hostname.");
            if (!rule.PathPrefix.StartsWith('/') || rule.PathPrefix.Contains("..", StringComparison.Ordinal))
                errors.Add("webAccess rule pathPrefix must start with '/' and cannot contain parent traversal.");
            if (string.IsNullOrWhiteSpace(rule.Purpose))
                errors.Add("webAccess rule purpose is required.");
            if (rule.Methods.Count == 0 ||
                rule.Methods.Any(x => x is not ("GET" or "HEAD" or "POST" or "PUT" or "PATCH" or "DELETE")))
                errors.Add("webAccess rule methods contains an unsupported HTTP method.");
            if (rule.Credential is not null)
            {
                if (!credentials.TryGetValue(rule.Credential, out var credential))
                    errors.Add($"webAccess rule references unknown credential '{rule.Credential}'.");
                else
                {
                    var port = rule.Port is null ? string.Empty : $":{rule.Port}";
                    var origin = $"{rule.Scheme}://{rule.Host}{port}";
                    if (!credential.AllowedOrigins.Contains(origin, StringComparer.OrdinalIgnoreCase))
                        errors.Add($"Credential '{rule.Credential}' is not bound to webAccess origin '{origin}'.");
                }
            }
            if (rule.Connection is not null)
            {
                var connection = (manifest.Connections ?? []).SingleOrDefault(x => x.Id == rule.Connection);
                if (connection is null)
                    errors.Add($"webAccess rule references unknown connection '{rule.Connection}'.");
                else
                {
                    var port = rule.Port is null ? string.Empty : $":{rule.Port}";
                    var origin = $"{rule.Scheme}://{rule.Host}{port}";
                    if (!connection.AllowedOrigins.Contains(origin, StringComparer.OrdinalIgnoreCase))
                        errors.Add($"Connection '{rule.Connection}' is not bound to webAccess origin '{origin}'.");
                }
            }
            if (rule.Credential is not null && rule.Connection is not null)
                errors.Add("A webAccess rule cannot use both credential and connection bindings.");
        }
    }

    private static void RequiredIdentifier(string? value, string field, ICollection<string> errors)
    {
        if (string.IsNullOrWhiteSpace(value) || !IdentifierPattern.IsMatch(value))
            errors.Add($"Plugin manifest {field} must contain letters, numbers, dots, underscores, or hyphens.");
    }

    private static void Required(string? value, string field, ICollection<string> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
            errors.Add($"Plugin manifest {field} is required.");
    }

    private static void ValidateNames(IEnumerable<string> names, string field, ICollection<string> errors)
    {
        var values = names.ToArray();
        if (values.Any(string.IsNullOrWhiteSpace))
            errors.Add($"Plugin manifest {field} must be an array of non-empty strings.");
        if (values.Distinct(StringComparer.Ordinal).Count() != values.Length)
            errors.Add($"Plugin manifest {field} must not contain duplicate names.");
    }
}
