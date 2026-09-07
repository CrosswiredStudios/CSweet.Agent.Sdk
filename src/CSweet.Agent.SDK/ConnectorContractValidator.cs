using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using CSweet.Agent.Contracts.Packaging;

namespace CSweet.Agent.SDK;

/// <summary>Shared importer/SDK validation of the protocol 2.1 security surface.</summary>
public static class ConnectorContractValidator
{
    private static readonly Regex Identifier = new("^[a-z][a-z0-9.-]{0,198}[a-z0-9]$", RegexOptions.CultureInvariant);
    private static readonly Regex Pointer = new("^(/[A-Za-z0-9_-]+)+$", RegexOptions.CultureInvariant);
    private static readonly HashSet<string> Effects = new(StringComparer.Ordinal)
        { "read", "write", "destructive", "irreversible", "live", "security-sensitive-write", "fiscal-write" };
    private static readonly HashSet<string> ReservedOAuthParameters = new(StringComparer.OrdinalIgnoreCase)
        { "client_id", "client_secret", "redirect_uri", "response_type", "scope", "state", "code_challenge",
          "code_challenge_method", "request", "request_uri", "redirect", "response_mode" };

    public static IReadOnlyList<string> Validate(AgentManifest manifest)
    {
        var errors = new List<string>();
        var dependencies = manifest.Dependencies ?? [];
        var operations = manifest.ProviderOperations.Where(x => x.Http is not null).ToArray();
        var enhanced = manifest.Kind == "connector" || dependencies.Count > 0 || operations.Length > 0 ||
            manifest.Connections.Any(x => x.Provider is not null) || manifest.Setup?.Assistance is not null ||
            manifest.Setup?.Flows.SelectMany(x => x.Steps).Any(x => x.AccountOptions is not null) == true;
        if (enhanced && manifest.Protocol?.MinimumVersion != "2.1")
            errors.Add("Connector contracts and dependencies require protocol minimumVersion 2.1.");
        if (manifest.Setup?.Assistance is { } assistance &&
            (manifest.Kind != "agent" || assistance.Profile != "conversation.v1" || !manifest.Setup.Required ||
             manifest.WebAccess.Mode != "None" || manifest.Runtime.WorkspaceAccess != "None"))
            errors.Add("Setup assistance requires an agent, required setup, conversation.v1, and no filesystem or raw network authority.");
        if (dependencies.Select(x => x.Id).Distinct(StringComparer.Ordinal).Count() != dependencies.Count)
            errors.Add("Dependency IDs must be unique.");
        foreach (var dependency in dependencies)
        {
            if (!Identifier.IsMatch(dependency.Id) || !Identifier.IsMatch(dependency.PluginId) ||
                !Identifier.IsMatch(dependency.PublisherId) || dependency.PluginId == manifest.Id)
                errors.Add("Dependencies require distinct, valid package, publisher and local IDs.");
            if (!Version.TryParse(dependency.MinimumVersion, out var min) || min.Build < 0 ||
                !Version.TryParse(dependency.MaximumVersionExclusive, out var max) || max.Build < 0 || min >= max)
                errors.Add($"Dependency '{dependency.Id}' requires a nonempty numeric semantic version range.");
        }
        foreach (var requirement in manifest.Requires.Where(x => x.Dependency is not null))
            if (!dependencies.Any(x => x.Id == requirement.Dependency) || requirement.Scope != "organization")
                errors.Add($"Required capability '{requirement.Name}' has an invalid dependency binding.");
        if (dependencies.Count > 0 && (manifest.Credentials.Count > 0 || manifest.Connections.Count > 0 ||
            manifest.WebAccess.Mode != "None"))
            errors.Add("Agents consuming connectors cannot also declare credential or raw network authority.");
        if (manifest.Kind == "connector")
        {
            if (manifest.Credentials.Count > 0 || manifest.WebAccess.Mode != "None" || manifest.McpServers.Count > 0 ||
                manifest.FileTransferTargets.Count > 0 || dependencies.Count > 0 || manifest.Runtime.WorkspaceAccess != "None")
                errors.Add("Connectors use only declared host-materialized operations, not raw credentials, network, or transitive dependencies.");
            if (manifest.RolePolicy is not null || manifest.Requires.Count > 0)
                errors.Add("Deterministic connectors cannot request agent roles or platform/model tools.");
            if (manifest.Connections.Count != 1 || !manifest.Runtime.SupportsMultipleInstallations)
                errors.Add("A connector must declare exactly one connection and support separate installations.");
            if (operations.Length == 0 || manifest.Provides.Any(x => !operations.Any(o => o.Capability == x.Name)))
                errors.Add("Every connector capability must have a closed HTTP operation mapping.");
        }
        foreach (var connection in manifest.Connections)
        {
            var provider = connection.Provider;
            if (manifest.Kind == "connector" && provider is null)
                errors.Add("Connector connections require public OAuth provider metadata.");
            if (provider is not null && (string.IsNullOrWhiteSpace(provider.DisplayName) ||
                !PublicEndpoint(provider.AuthorizationEndpoint) || !PublicEndpoint(provider.TokenEndpoint) ||
                !PublicEndpoint(provider.RevocationEndpoint) || provider.ClientAuthentication != "client_secret_post"))
                errors.Add("OAuth metadata requires public HTTPS endpoints and supported client authentication.");
            if (provider is not null && (provider.AuthorizationParameters.Count > 16 ||
                provider.AuthorizationParameters.Any(x => !Regex.IsMatch(x.Key, "^[a-z_]{1,64}$") ||
                    ReservedOAuthParameters.Contains(x.Key) || x.Value.Length > 256 ||
                    x.Value.Any(char.IsControl))))
                errors.Add("OAuth extension parameters must be bounded constants and cannot replace protocol/security fields.");
        }
        foreach (var step in manifest.Setup?.Flows.SelectMany(x => x.Steps) ?? [])
        {
            if (step.AccountOptions is not { } options) continue;
            var operation = operations.FirstOrDefault(x => x.Capability == step.Capability);
            if (manifest.Kind != "connector" || step.Kind is not ("account-selector" or "health-check") ||
                operation?.Http is not { Bootstrap: true } || operation.Http.Connection != step.Connection ||
                !Pointer.IsMatch(options.ItemsPointer) || !Pointer.IsMatch(options.IdPointer) ||
                !Pointer.IsMatch(options.NamePointer) || options.HandlePointer is not null && !Pointer.IsMatch(options.HandlePointer) ||
                options.NextPageTokenPointer is not null && !Pointer.IsMatch(options.NextPageTokenPointer))
                errors.Add("Account selection requires a declared read-only connector bootstrap operation and bounded JSON pointers.");
        }
        foreach (var operation in operations)
        {
            var http = operation.Http!;
            var matchingConnections = manifest.Connections.Where(x => x.Id == http.Connection).ToArray();
            var connection = matchingConnections.Length == 1 ? matchingConnections[0] : null;
            if (manifest.Kind != "connector" || connection is null || !Effects.Contains(operation.Effect))
                errors.Add($"Operation '{operation.Capability}' requires a connector, declared connection and known effect.");
            var provided = manifest.Provides.Where(x => x.Name == operation.Capability).ToArray();
            if (provided.Length != 1)
                errors.Add($"Operation '{operation.Capability}' is not provided by this connector.");
            else if (!JsonElement.DeepEquals(provided[0].InputSchema, operation.InputSchema) ||
                !JsonElement.DeepEquals(provided[0].OutputSchema, operation.OutputSchema) ||
                provided[0].Idempotency != operation.Idempotency)
                errors.Add("The tool and operation must expose identical schemas and idempotency.");
            if (!string.IsNullOrEmpty(operation.Credential) || !string.IsNullOrEmpty(operation.Command) ||
                !string.IsNullOrEmpty(operation.ProductionEndpoint) || !string.IsNullOrEmpty(operation.SandboxEndpoint) ||
                !string.IsNullOrEmpty(operation.ProviderProfile))
                errors.Add("Closed HTTP mappings cannot also declare legacy provider commands or credentials.");
            if (operation.Idempotency is not ("caller-key" or "none") ||
                operation.Effect != "read" && operation.Idempotency != "caller-key")
                errors.Add("Mutating connector operations require caller-key idempotency.");
            if (!AllowedEndpoint(http.Endpoint, connection) || http.Method is not ("GET" or "POST" or "PUT" or "PATCH" or "DELETE"))
                errors.Add($"Operation '{operation.Capability}' has an unsafe endpoint or method.");
            if (operation.Effect == "read" && http.Method != "GET" || http.Bootstrap && operation.Effect != "read")
                errors.Add("Read and bootstrap operations must be GET operations.");
            if (http.Bootstrap && (http.ResourceChecks.Count > 0 || http.MediaInput is not null || http.SecretResponseFields.Count > 0))
                errors.Add("Bootstrap reads cannot transfer media, extract secrets or validate unrelated input resources.");
            if (http.ScopeSets.Count == 0 || http.ScopeSets.Any(x => connection?.ScopeSets.Any(s => s.Id == x) != true))
                errors.Add("Each operation requires declared scope sets.");
            if (!ClosedSchema(operation.InputSchema) || operation.OutputSchema.ValueKind != JsonValueKind.Object)
                errors.Add("Connector operation inputs require exact object schemas.");
            if (http.QueryInputs.Values.Any(x => !Pointer.IsMatch(x)) ||
                http.BodyInputs.Any(x => !Pointer.IsMatch(x.Key) || !Pointer.IsMatch(x.Value)) ||
                http.QueryInputs.Keys.Intersect(http.QueryConstants.Keys).Any() ||
                http.BoundResourceQuery is not null && (http.QueryInputs.ContainsKey(http.BoundResourceQuery) || http.QueryConstants.ContainsKey(http.BoundResourceQuery)))
                errors.Add("Request mappings require distinct fields and bounded JSON pointers.");
            if (http.MediaInput is not null && (!Pointer.IsMatch(http.MediaInput) || http.Method != "POST" || operation.Effect == "read"))
                errors.Add("Media transfer requires a mutating POST and a typed media pointer.");
            if (http.BoundResourceQueryPrefix.Length > 64 || http.BoundResourceQueryPrefix.Any(char.IsControl) ||
                http.BoundResourceQueryPrefix.Length > 0 && http.BoundResourceQuery is null)
                errors.Add("A bounded literal resource prefix requires a host-bound query field.");
            if (http.SecretResponseFields.Any(x => !Pointer.IsMatch(x.Replace("/*/", "/0/", StringComparison.Ordinal))))
                errors.Add("Secret response fields require JSON pointers with optional array wildcards.");
            if (http.ResourceChecks.Count > 8) errors.Add("An operation can check at most eight resource relationships.");
            foreach (var check in http.ResourceChecks)
            if (!AllowedEndpoint(check.Endpoint, connection) ||
                !Pointer.IsMatch(check.InputPointer) || !Pointer.IsMatch(check.OwnerPointer) ||
                string.IsNullOrWhiteSpace(check.ResourceQuery) || check.QueryConstants.ContainsKey(check.ResourceQuery))
                errors.Add("Resource checks require an approved origin and exact input/owner pointers.");
        }
        return errors;
    }

    public static bool PublicEndpoint(string value) => Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        uri.Scheme == "https" && uri.IsDefaultPort && !uri.IsLoopback && string.IsNullOrEmpty(uri.UserInfo) &&
        string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment) && !IPAddress.TryParse(uri.Host, out _) &&
        uri.Host.Contains('.') && !uri.Host.EndsWith(".local", StringComparison.OrdinalIgnoreCase) &&
        !uri.Host.EndsWith(".internal", StringComparison.OrdinalIgnoreCase);

    private static bool AllowedEndpoint(string value, AgentConnectionDeclaration? connection) =>
        PublicEndpoint(value) && connection?.AllowedOrigins.Contains(new Uri(value).GetLeftPart(UriPartial.Authority), StringComparer.Ordinal) == true;
    private static bool ClosedSchema(JsonElement schema) => schema.ValueKind == JsonValueKind.Object &&
        schema.TryGetProperty("type", out var type) && type.GetString() == "object" &&
        schema.TryGetProperty("additionalProperties", out var additional) && additional.ValueKind == JsonValueKind.False;
}
