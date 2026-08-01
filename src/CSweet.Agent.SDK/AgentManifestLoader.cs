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
        Protocol = manifest.Protocol,
        Provides = manifest.Provides ?? [],
        Requires = manifest.Requires ?? [],
        Events = manifest.Events ?? new AgentEventManifest([]),
        Configuration = manifest.Configuration ?? [],
        Credentials = manifest.Credentials ?? [],
        WebAccess = manifest.WebAccess ?? new AgentWebAccessManifest(),
        Ui = manifest.Ui ?? [],
        Catalog = manifest.Catalog ?? new AgentCatalogMetadata(),
        Capabilities = (manifest.Provides ?? []).Select(x => x.Name).ToArray(),
        RequestedCapabilities = (manifest.Requires ?? []).Select(x => x.Name).ToArray(),
        RequestedSubscriptions = manifest.Events?.Subscribes ?? [],
        RequestedNetworkAccess = manifest.RequestedNetworkAccess
    };

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
        if (runtime.DefaultActivationMode is not ("AlwaysOn" or "Periodic" or "Manual"))
            errors.Add("runtime.defaultActivationMode must be AlwaysOn, Periodic, or Manual.");
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
