using System.Net;
using System.Text.RegularExpressions;
using CSweet.Agent.Contracts.Packaging;
namespace CSweet.Agent.Contracts.Packaging
{
    /// <summary>
    /// Public declarations only. Keys and account-selection arguments are never part of the authoring contract.
    /// The trusted host derives its endpoint, header, tool mapping and schema enforcement from this reviewed
    /// declaration, pinned by the installed package digest.
    /// </summary>
    public sealed record ReviewedRemoteMcpManifest
    {
        public string Profile { get; init; } = "";
        public string Endpoint { get; init; } = "";
        /// <summary><c>api-key</c> sends the key verbatim in <see cref="Header"/>; <c>bearer</c> sends <c>Authorization: Bearer</c>.</summary>
        public string Authentication { get; init; } = "";
        public string Header { get; init; } = "";
        public string AccountBinding { get; init; } = "";
        public string Transport { get; init; } = "";
        public IReadOnlyList<string> ProtocolVersions { get; init; } = [];
        /// <summary>Exactly one mapping per provided capability. Results are a single MCP text content item containing JSON.</summary>
        public IReadOnlyList<ReviewedRemoteMcpTool> Tools { get; init; } = [];
    }

    /// <summary>Maps one reviewed C-Sweet capability to one remote MCP tool.</summary>
    public sealed record ReviewedRemoteMcpTool
    {
        public string Capability { get; init; } = "";
        public string RemoteTool { get; init; } = "";
        /// <summary>Only <c>read</c> is supported. Writes need a separately reviewed idempotency and approval design.</summary>
        public string Effect { get; init; } = "";
    }
}
namespace CSweet.Agent.SDK
{
    /// <summary>
    /// Validates the publisher-declared reviewed remote MCP mode (protocol 2.6). Identity is not pinned: any
    /// publisher may declare an endpoint, which a human approves at install review. The declaration must stay
    /// closed, bounded, read-only and free of executable, network, credential or configuration authority.
    /// </summary>
    public static class ReviewedRemoteMcpValidator
    {
        public const string RequiredProtocolMinimum = "2.6";
        public const string AccountBinding = "owner-business-installation.v1";
        public const string Transport = "stateless-post";
        public const int MaximumExecutionTimeoutSeconds = 30;
        public const int MaximumTools = 32;

        /// <summary>MCP protocol revisions the trusted broker implements.</summary>
        public static IReadOnlySet<string> SupportedMcpProtocolVersions { get; } = new HashSet<string>(StringComparer.Ordinal) { "2025-03-26" };

        /// <summary>Protocol minimums reserved for reviewed remote MCP; other kinds may not declare them.</summary>
        public static IReadOnlySet<string> ReservedProtocolMinimums { get; } = new HashSet<string>(StringComparer.Ordinal) { "2.5", "2.6" };

        private static readonly Regex Profile = new("^[a-z][a-z0-9-]{0,62}(\\.[a-z0-9-]{1,62}){0,4}\\.v[1-9][0-9]{0,3}$", RegexOptions.CultureInvariant);
        private static readonly Regex RemoteTool = new("^[A-Za-z][A-Za-z0-9_.-]{0,127}$", RegexOptions.CultureInvariant);
        private static readonly Regex ApiKeyHeader = new("^X-[A-Za-z0-9][A-Za-z0-9-]{0,39}$", RegexOptions.CultureInvariant);
        private static readonly Regex HostLabel = new("^[a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?$", RegexOptions.CultureInvariant);

        public static IReadOnlyList<string> Validate(AgentManifest manifest)
        {
            if (manifest.Protocol is null || manifest.Runtime is null) return ["Protocol and runtime declarations are required."];
            if (manifest.Kind != "remote-mcp")
                return manifest.ReviewedRemoteMcp is null && !ReservedProtocolMinimums.Contains(manifest.Protocol.MinimumVersion ?? "") ? []
                    : ["reviewedRemoteMcp and protocol minimums 2.5/2.6 are reserved for kind remote-mcp."];

            var errors = new List<string>();
            if (manifest.Protocol.MinimumVersion == "2.5")
                return ["This reviewed remote MCP manifest uses the retired closed 2.5 profile. Update it to protocol 2.6 with a reviewedRemoteMcp.tools mapping."];
            if (manifest.ManifestVersion != "2.0" || manifest.Protocol.MinimumVersion != RequiredProtocolMinimum)
                errors.Add($"Reviewed remote MCP requires manifestVersion 2.0 and protocol minimumVersion {RequiredProtocolMinimum}.");
            if (manifest.Runtime.Type != "host-remote-mcp" || manifest.Runtime.ProjectPath is not null ||
                manifest.Runtime.WorkspaceAccess != "None" || manifest.Runtime.SupportsMultipleInstallations)
                errors.Add("Reviewed remote MCP runs in the trusted host: runtime.type host-remote-mcp, no project, no workspace access, one installation.");
            if (manifest.Requires.Count != 0 || manifest.Dependencies.Count != 0 || manifest.Configuration.Count != 0 ||
                manifest.Connections.Count != 0 || manifest.Credentials.Count != 0 || manifest.McpServers.Count != 0 ||
                manifest.ProviderOperations.Count != 0 || manifest.FileTransferTargets.Count != 0 || manifest.Accounts is not null ||
                manifest.Setup is not null || manifest.RolePolicy is not null || manifest.WebAccess.Mode != "None" || manifest.WebAccess.Rules.Count != 0 ||
                manifest.Events.Subscribes.Count != 0 || manifest.Ui.Count != 0)
                errors.Add("Reviewed remote MCP declares no requirements, dependencies, configuration, credentials, connections, provider operations, accounts, setup, role, web access, events or UI.");

            var d = manifest.ReviewedRemoteMcp;
            if (d is null) { errors.Add("Reviewed remote MCP requires a reviewedRemoteMcp declaration."); return errors; }
            if (!Profile.IsMatch(d.Profile ?? "")) errors.Add("reviewedRemoteMcp.profile must be a lowercase dotted identifier ending in a version such as acme.read.v1.");
            ValidateEndpoint(d.Endpoint, errors);
            if (d.Authentication == "api-key")
            {
                if (!ApiKeyHeader.IsMatch(d.Header ?? "")) errors.Add("api-key authentication requires an X- prefixed header name of at most 42 characters.");
            }
            else if (d.Authentication == "bearer")
            {
                if (d.Header != "Authorization") errors.Add("bearer authentication requires header Authorization.");
            }
            else errors.Add("reviewedRemoteMcp.authentication must be api-key or bearer.");
            if (d.AccountBinding != AccountBinding) errors.Add($"reviewedRemoteMcp.accountBinding must be {AccountBinding}.");
            if (d.Transport != Transport) errors.Add($"reviewedRemoteMcp.transport must be {Transport}.");
            if (d.ProtocolVersions.Count == 0 || d.ProtocolVersions.Distinct(StringComparer.Ordinal).Count() != d.ProtocolVersions.Count ||
                d.ProtocolVersions.Any(v => !SupportedMcpProtocolVersions.Contains(v)))
                errors.Add($"reviewedRemoteMcp.protocolVersions must be unique values from: {string.Join(", ", SupportedMcpProtocolVersions)}.");

            var provides = manifest.Provides;
            if (provides.Count == 0 || provides.Count > MaximumTools) errors.Add($"Reviewed remote MCP must provide 1 to {MaximumTools} capabilities.");
            if (d.Tools.Count != provides.Count ||
                d.Tools.Select(t => t.Capability).Distinct(StringComparer.Ordinal).Count() != d.Tools.Count ||
                d.Tools.Select(t => t.RemoteTool).Distinct(StringComparer.Ordinal).Count() != d.Tools.Count ||
                !d.Tools.Select(t => t.Capability).Order(StringComparer.Ordinal).SequenceEqual(provides.Select(p => p.Name).Order(StringComparer.Ordinal)))
                errors.Add("reviewedRemoteMcp.tools must map each provided capability to exactly one unique remote tool.");
            foreach (var tool in d.Tools)
            {
                if (!RemoteTool.IsMatch(tool.RemoteTool ?? "")) errors.Add($"Remote tool name for '{tool.Capability}' is invalid.");
                if (tool.Effect != "read") errors.Add($"Remote tool '{tool.RemoteTool}' must declare effect read; remote writes are not supported in this mode.");
            }
            foreach (var capability in provides)
            {
                if (capability.Idempotency != "none" || capability.ExecutionTimeoutSeconds is < 1 or > MaximumExecutionTimeoutSeconds)
                    errors.Add($"Capability '{capability.Name}' must use idempotency none and a timeout of at most {MaximumExecutionTimeoutSeconds} seconds.");
                ClosedJsonSchema.ValidateDefinition(capability.InputSchema, $"{capability.Name}.inputSchema", errors, requireObjectRoot: true);
                ClosedJsonSchema.ValidateDefinition(capability.OutputSchema, $"{capability.Name}.outputSchema", errors);
            }
            return errors;
        }

        /// <summary>Returns the lowercase DNS host of a valid reviewed endpoint, or throws.</summary>
        public static string EndpointHost(string endpoint)
        {
            var errors = new List<string>();
            var host = ValidateEndpoint(endpoint, errors);
            return errors.Count == 0 ? host! : throw new InvalidOperationException(string.Join(" ", errors));
        }

        private static string? ValidateEndpoint(string? endpoint, ICollection<string> errors)
        {
            const string message = "reviewedRemoteMcp.endpoint must be an https URL on port 443 with a public DNS host, a path, and no credentials, query or fragment.";
            if (string.IsNullOrEmpty(endpoint) || endpoint.Length > 256 || endpoint.Any(c => c > 0x7E || char.IsControl(c) || char.IsWhiteSpace(c)) ||
                !Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps || uri.Port != 443 ||
                !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment) ||
                uri.AbsolutePath is "" or "/" || uri.HostNameType != UriHostNameType.Dns || IPAddress.TryParse(uri.Host, out _) ||
                !endpoint.StartsWith($"https://{uri.Host}/", StringComparison.OrdinalIgnoreCase))
            { errors.Add(message); return null; }
            var host = uri.Host.ToLowerInvariant();
            var labels = host.Split('.');
            if (labels.Length < 2 || labels.Any(l => !HostLabel.IsMatch(l)) || labels[^1].All(char.IsAsciiDigit) ||
                host is "localhost" || host.EndsWith(".localhost", StringComparison.Ordinal) || host.EndsWith(".local", StringComparison.Ordinal) ||
                host.EndsWith(".internal", StringComparison.Ordinal) || host.EndsWith(".home.arpa", StringComparison.Ordinal))
            { errors.Add(message); return null; }
            return host;
        }
    }
}
