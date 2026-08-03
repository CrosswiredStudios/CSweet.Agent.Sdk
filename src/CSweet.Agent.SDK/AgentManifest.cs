using System.Text.Json;

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
    public AgentWebAccessManifest WebAccess { get; init; } = new();
    public IReadOnlyList<AgentUiContribution> Ui { get; init; } = [];
    public AgentCatalogMetadata Catalog { get; init; } = new();
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
    public string? DefaultActivationMode { get; init; } = "Manual";
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
}

/// <summary>A named credential binding; credential values are never included in a manifest.</summary>
public sealed record AgentCredentialBinding
{
    public string Name { get; init; } = string.Empty;
    public string Type { get; init; } = string.Empty;
    public IReadOnlyList<string> AllowedOrigins { get; init; } = [];
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
}

/// <summary>A UI contribution backed by a capability declared in the same manifest.</summary>
public sealed record AgentUiContribution
{
    public string Kind { get; init; } = string.Empty;
    public string Id { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string? Capability { get; init; }
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
