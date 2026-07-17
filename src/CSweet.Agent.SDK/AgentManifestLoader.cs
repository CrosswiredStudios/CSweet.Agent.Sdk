using System.Text.Json;
using CSweet.Agent.Contracts.Packaging;

namespace CSweet.Agent.SDK;

public static class AgentManifestLoader
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static async Task<AgentManifest> LoadAsync(
        string manifestPath,
        CancellationToken cancellationToken)
    {
        var resolvedPath = Path.IsPathRooted(manifestPath)
            ? manifestPath
            : Path.Combine(AppContext.BaseDirectory, manifestPath);

        if (!File.Exists(resolvedPath))
        {
            throw new FileNotFoundException(
                $"Agent manifest was not found at '{resolvedPath}'.",
                resolvedPath);
        }

        var json = await File.ReadAllTextAsync(resolvedPath, cancellationToken);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var manifest = root.TryGetProperty("kind", out _)
            ? ReadCanonicalPluginManifest(root)
            : JsonSerializer.Deserialize<AgentManifest>(json, SerializerOptions);

        if (manifest is null)
        {
            throw new InvalidOperationException("Agent manifest could not be deserialized.");
        }

        Validate(manifest);
        return manifest;
    }

    private static AgentManifest ReadCanonicalPluginManifest(JsonElement root)
    {
        var kind = root.GetProperty("kind").GetString();
        if (root.GetProperty("manifestVersion").GetString() != "1.0" || kind is not ("agent" or "service"))
            throw new InvalidOperationException("Plugin manifest must use version 1.0 and kind agent or service.");
        var publisher = root.GetProperty("publisher");
        var runtime = root.GetProperty("runtime");
        var protocol = root.GetProperty("protocol");
        var events = root.GetProperty("events");
        var provides = root.GetProperty("provides").EnumerateArray()
            .Select(x => new AgentProvidedCapability(x.GetProperty("name").GetString()!)).ToArray();
        var requires = root.TryGetProperty("requires", out var requiredCapabilities)
            ? requiredCapabilities.EnumerateArray().Select(x => new AgentRequiredCapability(
                x.GetProperty("name").GetString()!,
                x.TryGetProperty("scope", out var scope) ? scope.GetString() : null,
                x.TryGetProperty("purpose", out var purpose) ? purpose.GetString() : null)).ToArray()
            : [];
        var subscribes = events.GetProperty("subscribes").EnumerateArray().Select(x => x.GetString()!).ToArray();
        var publishes = events.GetProperty("publishes").EnumerateArray().Select(x => x.GetString()!).ToArray();
        return new AgentManifest
        {
            ManifestVersion = "1.0",
            Kind = kind!,
            Id = root.GetProperty("id").GetString()!,
            Name = root.GetProperty("name").GetString()!,
            Version = root.GetProperty("version").GetString()!,
            Publisher = new AgentPublisher(publisher.GetProperty("id").GetString()!, publisher.GetProperty("name").GetString()!),
            Runtime = new AgentRuntimeManifest
            {
                Type = runtime.GetProperty("type").GetString()!,
                ProjectPath = runtime.TryGetProperty("projectPath", out var projectPath) ? projectPath.GetString() : null,
                TargetFramework = runtime.TryGetProperty("targetFramework", out var framework) ? framework.GetString() : null,
                DefaultActivationMode = runtime.TryGetProperty("defaultActivationMode", out var activation) ? activation.GetString() : null,
                SupportsMultipleInstallations = runtime.TryGetProperty("supportsMultipleInstallations", out var multi) && multi.GetBoolean(),
                MaximumConcurrentJobs = runtime.TryGetProperty("maximumConcurrentJobs", out var jobs) ? jobs.GetInt32() : 1
            },
            Protocol = new AgentProtocolManifest(protocol.GetProperty("minimumVersion").GetString()!, protocol.GetProperty("maximumVersion").GetString()!),
            Capabilities = provides.Select(x => x.Name).ToArray(),
            RequestedSubscriptions = subscribes,
            RequestedPublications = publishes,
            RequestedCapabilities = requires.Select(x => x.Name).ToArray(),
            Provides = provides,
            Requires = requires,
            Events = new AgentEventManifest(subscribes, publishes),
            RequestedPermissions = []
        };
    }

    private static void Validate(AgentManifest manifest)
    {
        if (string.IsNullOrWhiteSpace(manifest.Id))
        {
            throw new InvalidOperationException("Agent manifest id is required.");
        }

        if (string.IsNullOrWhiteSpace(manifest.Version))
        {
            throw new InvalidOperationException("Agent manifest version is required.");
        }

        if (manifest.Runtime.MaximumConcurrentJobs < 1)
        {
            throw new InvalidOperationException(
                "Agent manifest runtime.maximumConcurrentJobs must be at least one.");
        }
    }
}
