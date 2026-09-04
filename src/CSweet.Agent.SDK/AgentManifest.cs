using System.Text.Json;
using CSweet.WorkManagement.Contracts;

namespace CSweet.Agent.Contracts.Packaging;

/// <summary>Describes one protocol-v2 C-Sweet executable package.</summary>
public sealed class AgentManifest
{
    public string ManifestVersion { get; init; } = "2.0";
    public string Kind { get; init; } = "agent";
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Version { get; init; }
    public required AgentPublisher Publisher { get; init; }
    public required AgentRuntimeManifest Runtime { get; init; }
    public AgentRolePolicyManifest? RolePolicy { get; init; }
    public AgentWorkItemTypesManifest WorkItemTypes { get; init; } = new();
    public WorkstreamProfileManifest WorkstreamProfiles { get; init; } = new([], []);
    public ToolchainAdapterManifest ToolchainAdapters { get; init; } = new([], []);
    public required AgentProtocolManifest Protocol { get; init; }

    /// <summary>Compatibility projection of the names declared in <see cref="Provides"/>.</summary>
    public IReadOnlyList<string> Capabilities { get; init; } = [];

    /// <summary>Compatibility projection of the event names declared in <see cref="Events"/>.</summary>
    public IReadOnlyList<string> RequestedSubscriptions { get; init; } = [];

    /// <summary>Compatibility projection of the names declared in <see cref="Requires"/>.</summary>
    public IReadOnlyList<string> RequestedCapabilities { get; init; } = [];

    /// <summary>Legacy compatibility projection. Use <see cref="WebAccess"/> for new manifests.</summary>
    public IReadOnlyList<string> RequestedNetworkAccess { get; init; } = [];

    public IReadOnlyList<AgentProvidedCapability> Provides { get; init; } = [];
    public IReadOnlyList<AgentRequiredCapability> Requires { get; init; } = [];
    public AgentEventManifest Events { get; init; } = new([]);
    public IReadOnlyList<AgentManifestConfigurationField> Configuration { get; init; } = [];
    public IReadOnlyList<AgentCredentialBinding> Credentials { get; init; } = [];
    public IReadOnlyList<AgentConnectionDeclaration> Connections { get; init; } = [];
    public IReadOnlyList<AgentMcpServerDeclaration> McpServers { get; init; } = [];
    public IReadOnlyList<AgentProviderOperationDeclaration> ProviderOperations { get; init; } = [];
    public IReadOnlyList<AgentFileTransferTargetDeclaration> FileTransferTargets { get; init; } = [];
    public AgentSetupManifest? Setup { get; init; }
    public AgentWebAccessManifest WebAccess { get; init; } = new();
    public IReadOnlyList<AgentUiContribution> Ui { get; init; } = [];
    public AgentCatalogMetadata Catalog { get; init; } = new();
}

/// <summary>Stable work types that must be supplied by an installed platform provider.</summary>
public sealed record AgentWorkItemTypesManifest
{
    public IReadOnlyList<string> Requires { get; init; } = [];
}

/// <summary>Declares how an agent operates and which stable organizational roles it can fill.</summary>
public sealed record AgentRolePolicyManifest
{
    public string Profile { get; init; } = string.Empty;
    /// <summary>Stable high-level role categories, for example software-architect.</summary>
    public IReadOnlyList<string> DeclaredRoleKeys { get; init; } = [];
    /// <summary>Optional domain strengths used to rank otherwise eligible agents.</summary>
    public IReadOnlyList<string> SpecializationKeys { get; init; } = [];
}

/// <summary>A capability implemented by this package.</summary>
public sealed record AgentProvidedCapability(
    string Name,
    string Description,
    JsonElement InputSchema,
    JsonElement OutputSchema,
    int ExecutionTimeoutSeconds,
    string Idempotency,
    string RiskClass = "standard",
    string? DescriptorHash = null);

/// <summary>A platform or provider capability requested by this package.</summary>
public sealed record AgentRequiredCapability(
    string Name,
    string? Scope = "organization",
    string? Purpose = null);

/// <summary>Event subscriptions for this package. Protocol v2 does not allow generic publications.</summary>
public sealed record AgentEventManifest(IReadOnlyList<string> Subscribes)
{
    public IReadOnlyList<string> Publishes { get; init; } = [];
}

/// <summary>Publisher identity displayed during installation review.</summary>
public sealed record AgentPublisher(string Id, string Name);

/// <summary>Executable runtime configuration for a package.</summary>
public sealed class AgentRuntimeManifest
{
    public string Type { get; init; } = "dotnet-project";
    public string? ProjectPath { get; init; }
    public string? TargetFramework { get; init; }
    public string? DefaultActivationMode { get; init; } = "OnDemand";
    /// <summary>Preferred platform-owned attention cadence for this package.</summary>
    public int? DefaultTickFrequencySeconds { get; init; }
    public IReadOnlyDictionary<string, string> Entrypoints { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    public bool SupportsMultipleInstallations { get; init; }
    public int MaximumConcurrentJobs { get; init; } = 1;
    /// <summary>Platform-approved container image/profile requested by this package.</summary>
    public string? EnvironmentProfile { get; init; }
    /// <summary>Requested workspace access. Supported values are None, ReadOnly, and ReadWrite.</summary>
    public string WorkspaceAccess { get; init; } = "None";
}

/// <summary>Supported private runtime protocol range.</summary>
public sealed record AgentProtocolManifest(string MinimumVersion, string MaximumVersion);

/// <summary>One installation-time configuration field exposed by the package.</summary>
public sealed record AgentManifestConfigurationField
{
    public string Key { get; init; } = string.Empty;
    public string Type { get; init; } = "string";
    public string Label { get; init; } = string.Empty;
    public bool Required { get; init; }
    public bool Secret { get; init; }
    /// <summary>Optional scalar value used to initialize a new installation.</summary>
    public JsonElement? DefaultValue { get; init; }
    /// <summary>Allowed values for select fields.</summary>
    public IReadOnlyList<AgentManifestConfigurationOption>? Options { get; init; }
    /// <summary>For numeric fields, requires this value to be strictly less than the referenced numeric field.</summary>
    public string? LessThanFieldKey { get; init; }
    /// <summary>Field whose string value controls whether this field is shown and required.</summary>
    public string? VisibleWhenFieldKey { get; init; }
    /// <summary>Exact controller value that makes this field visible.</summary>
    public string? VisibleWhenValue { get; init; }
}

/// <summary>One allowed value for a select configuration field.</summary>
public sealed record AgentManifestConfigurationOption(string Value, string Label);

/// <summary>A named credential binding; credential values are never included in a manifest.</summary>
public sealed record AgentCredentialBinding
{
    public string Name { get; init; } = string.Empty;
    public string Type { get; init; } = string.Empty;
    public IReadOnlyList<string> AllowedOrigins { get; init; } = [];
}

/// <summary>A platform-brokered provider connection. Secrets and tokens never enter plugin runtime.</summary>
public sealed record AgentConnectionDeclaration
{
    public string Id { get; init; } = string.Empty;
    public string Type { get; init; } = "oauth2";
    public string ProviderProfile { get; init; } = string.Empty;
    public IReadOnlyList<string> AllowedOrigins { get; init; } = [];
    public IReadOnlyList<AgentConnectionScopeSet> ScopeSets { get; init; } = [];
    public IReadOnlyList<string> SecretResponseFields { get; init; } = [];
}

/// <summary>A named, user-consented permission set for progressive authorization.</summary>
public sealed record AgentConnectionScopeSet
{
    public string Id { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
    public string Purpose { get; init; } = string.Empty;
    public bool Required { get; init; }
    public IReadOnlyList<string> Scopes { get; init; } = [];
}

/// <summary>A remote MCP server reached only through the C-Sweet credential and transport broker.</summary>
public sealed record AgentMcpServerDeclaration
{
    public string Id { get; init; } = string.Empty;
    public string Endpoint { get; init; } = string.Empty;
    public string Transport { get; init; } = "streamable-http";
    public string Connection { get; init; } = string.Empty;
    public IReadOnlyList<string> ProtocolVersions { get; init; } = [];
    public IReadOnlyList<AgentMcpToolDeclaration> Tools { get; init; } = [];
}

/// <summary>An exact remote MCP tool projected as one local, grant-governed capability.</summary>
public sealed record AgentMcpToolDeclaration
{
    public string Capability { get; init; } = string.Empty;
    public string RemoteName { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public JsonElement InputSchema { get; init; }
    public JsonElement OutputSchema { get; init; }
    public string DescriptorHash { get; init; } = string.Empty;
    public string Effect { get; init; } = "read";
}

/// <summary>An exact legacy provider command materialized and invoked only by C-Sweet.</summary>
public sealed record AgentProviderOperationDeclaration
{
    public string Capability { get; init; } = string.Empty;
    public string ProviderProfile { get; init; } = string.Empty;
    public string Command { get; init; } = string.Empty;
    public string ProductionEndpoint { get; init; } = string.Empty;
    public string? SandboxEndpoint { get; init; }
    public string Credential { get; init; } = string.Empty;
    public JsonElement InputSchema { get; init; }
    public JsonElement OutputSchema { get; init; }
    public string Effect { get; init; } = "read";
    public string Idempotency { get; init; } = "none";
}

/// <summary>A path-confined file-transfer target; it never grants shell or arbitrary filesystem access.</summary>
public sealed record AgentFileTransferTargetDeclaration
{
    public string Id { get; init; } = string.Empty;
    public string Protocol { get; init; } = "sftp";
    public string Credential { get; init; } = string.Empty;
    public IReadOnlyList<string> AllowedHostSuffixes { get; init; } = [];
    public int Port { get; init; } = 22;
    public string RootPath { get; init; } = string.Empty;
    public IReadOnlyList<string> Operations { get; init; } = [];
}

/// <summary>A declarative, resumable setup graph rendered entirely by C-Sweet.</summary>
public sealed record AgentSetupManifest
{
    public bool Required { get; init; } = true;
    public string EntryFlow { get; init; } = string.Empty;
    public IReadOnlyList<AgentSetupFlow> Flows { get; init; } = [];
}

/// <summary>A sequence of safe, platform-rendered setup steps.</summary>
public sealed record AgentSetupFlow
{
    public string Id { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public IReadOnlyList<AgentSetupStep> Steps { get; init; } = [];
}

/// <summary>One declarative setup step. No executable markup or expressions are accepted.</summary>
public sealed record AgentSetupStep
{
    public string Id { get; init; } = string.Empty;
    public string Kind { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string? Connection { get; init; }
    public string? ScopeSet { get; init; }
    public string? Capability { get; init; }
    public IReadOnlyList<string> ConfigurationKeys { get; init; } = [];
}

/// <summary>Brokered outbound network policy requested by the package.</summary>
public sealed record AgentWebAccessManifest
{
    public string Mode { get; init; } = "None";
    public IReadOnlyList<AgentWebAccessRule> Rules { get; init; } = [];
    public string? Purpose { get; init; }
}

/// <summary>One broker-enforced outbound network allowlist rule.</summary>
public sealed record AgentWebAccessRule
{
    public string Scheme { get; init; } = "https";
    public string Host { get; init; } = string.Empty;
    public int? Port { get; init; }
    public string PathPrefix { get; init; } = "/";
    public IReadOnlyList<string> Methods { get; init; } = ["GET"];
    public string Protocol { get; init; } = "http";
    public string Purpose { get; init; } = string.Empty;
    public string? Credential { get; init; }
    public string? Connection { get; init; }
    public bool Bootstrap { get; init; }
}

/// <summary>A UI contribution backed by a capability declared in the same manifest.</summary>
public sealed record AgentUiContribution
{
    public string Kind { get; init; } = string.Empty;
    public string Id { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string? Capability { get; init; }
    public string? Flow { get; init; }
}

/// <summary>Optional discovery metadata for catalogs and marketplaces.</summary>
public sealed record AgentCatalogMetadata
{
    public string? Summary { get; init; }
    public string? Category { get; init; }
    public IReadOnlyList<string> RoleAliases { get; init; } = [];
    public IReadOnlyList<string> Keywords { get; init; } = [];
    public string? DocumentationUrl { get; init; }
}
