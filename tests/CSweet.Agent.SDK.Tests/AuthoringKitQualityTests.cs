using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CSweet.Agent.SDK.Tests;

public sealed partial class AuthoringKitQualityTests
{
    private static readonly string[] MaintainedDocuments =
    [
        "README.md",
        "AGENTS.md",
        "AGENT_AUTHORING.md",
        "GRANTS.md",
        "docs/creating-an-agent.md",
        "docs/manifest-reference.md",
        "docs/capabilities-and-events.md",
        "docs/testing-and-release.md",
        "docs/runtime-maintainers.md",
        "docs/migrating-to-1.0.md",
        "docs/migrating-to-2.0.md",
        "SECURITY.md",
        "python/README.md"
    ];

    [Fact]
    public void MaintainedDocumentation_HasNoBrokenLocalLinks()
    {
        var root = RepositoryRoot();
        var missing = new List<string>();
        foreach (var relative in MaintainedDocuments)
        {
            var path = Path.Combine(root, relative);
            Assert.True(File.Exists(path), $"Missing maintained document: {relative}");
            var directory = Path.GetDirectoryName(path)!;
            foreach (Match match in MarkdownLink().Matches(File.ReadAllText(path)))
            {
                var target = match.Groups["target"].Value.Trim('<', '>');
                if (target.StartsWith('#') || Uri.TryCreate(target, UriKind.Absolute, out _))
                    continue;
                target = target.Split('#', 2)[0].Replace('/', Path.DirectorySeparatorChar);
                if (target.Length > 0 && !File.Exists(Path.GetFullPath(Path.Combine(directory, target))))
                    missing.Add($"{relative} -> {target}");
            }
        }

        Assert.Empty(missing);
    }

    [Fact]
    public void Version_IsSynchronizedAcrossAuthoringSurfaces()
    {
        var root = RepositoryRoot();
        const string version = "3.13.0";
        var files = new[]
        {
            "src/CSweet.Agent.SDK/CSweet.Agent.SDK.csproj",
            "README.md",
            "AGENT_AUTHORING.md",
            "docs/creating-an-agent.md",
            "scripts/verify-authoring-template.ps1",
            "templates/CSweet.Agent.Template/.template.config/template.json",
            "templates/CSweet.Agent.Template/README.md",
            "templates/CSweet.Agent.Template/src/CSweet.Agent.Template/CSweet.Agent.Template.csproj"
        };

        foreach (var relative in files)
            Assert.Contains(version, File.ReadAllText(Path.Combine(root, relative)), StringComparison.Ordinal);
    }

    [Fact]
    public async Task GoldenManifests_AreValidAndCustomCapabilitiesAreAccepted()
    {
        var root = RepositoryRoot();
        foreach (var relative in new[]
                 {
                     "templates/CSweet.Agent.Template/csweet-plugin.json",
                     "samples/HelloAgent/csweet-plugin.json"
                 })
        {
            var manifest = await AgentManifestLoader.LoadAsync(
                Path.Combine(root, relative),
                CancellationToken.None);
            Assert.NotEmpty(manifest.Provides);
            Assert.All(manifest.Provides, item => Assert.EndsWith(".v1", item.Name, StringComparison.Ordinal));
        }
    }

    [Fact]
    public void ManifestSchema_TracksValidatorLimitsAndCanonicalSections()
    {
        var root = RepositoryRoot();
        var schemaPath = Path.Combine(root, "schemas", "csweet-plugin.v2.schema.json");
        using var schema = JsonDocument.Parse(File.ReadAllText(schemaPath));
        var text = schema.RootElement.GetRawText();

        var providedProperties = schema.RootElement.GetProperty("$defs")
            .GetProperty("providedCapability")
            .GetProperty("properties");
        Assert.Equal(86400, providedProperties.GetProperty("executionTimeoutSeconds").GetProperty("maximum").GetInt32());
        Assert.True(schema.RootElement.GetProperty("$defs")
            .GetProperty("configurationField")
            .GetProperty("properties")
            .TryGetProperty("defaultValue", out _));
        Assert.Contains("\"work-item\"", text, StringComparison.Ordinal);
        Assert.Contains("\"caller-key\"", text, StringComparison.Ordinal);
        foreach (var section in new[] { "configuration", "credentials", "webAccess", "ui", "catalog" })
            Assert.True(schema.RootElement.GetProperty("properties").TryGetProperty(section, out _), section);
    }

    [Fact]
    public void CapabilityAndEventReferences_AreCurrent()
    {
        var root = RepositoryRoot();
        var grants = File.ReadAllText(Path.Combine(root, "GRANTS.md"));
        foreach (var capability in CapabilityCatalog.All)
            Assert.Contains($"`{capability}`", grants, StringComparison.Ordinal);

        var events = File.ReadAllText(Path.Combine(root, "docs", "capabilities-and-events.md"));
        foreach (var type in new[] { typeof(HiringEvents), typeof(ManagementEvents) })
            foreach (var field in type.GetFields(
                         System.Reflection.BindingFlags.Public |
                         System.Reflection.BindingFlags.Static))
                Assert.Contains($"{type.Name}.{field.Name}", events, StringComparison.Ordinal);
    }

    [Fact]
    public void AuthoringSurfaces_DoNotTeachPrivateTransportOrUnsupportedPython()
    {
        var root = RepositoryRoot();
        var currentAuthoringFiles = new[]
        {
            "README.md",
            "AGENT_AUTHORING.md",
            "docs/creating-an-agent.md",
            "templates/CSweet.Agent.Template/README.md",
            "templates/CSweet.Agent.Template/AGENTS.md"
        };
        foreach (var relative in currentAuthoringFiles)
        {
            var text = File.ReadAllText(Path.Combine(root, relative));
            Assert.DoesNotContain("IAgentRuntimeTransport", text, StringComparison.Ordinal);
            Assert.DoesNotContain("workload token environment", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("connect to /mcp", text, StringComparison.OrdinalIgnoreCase);
        }

        var python = File.ReadAllText(Path.Combine(root, "python", "README.md"));
        Assert.Contains("not supported", python, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("expires_at", python, StringComparison.Ordinal);
    }

    [Fact]
    public void Template_UsesPinnedPackageRatherThanSdkSourceReference()
    {
        var root = RepositoryRoot();
        var project = File.ReadAllText(Path.Combine(
            root,
            "templates",
            "CSweet.Agent.Template",
            "src",
            "CSweet.Agent.Template",
            "CSweet.Agent.Template.csproj"));
        Assert.Contains("<PackageReference Include=\"CSweet.Agent.SDK\" Version=\"3.13.0\"", project);
        Assert.DoesNotContain("<ProjectReference", project);
    }

    [Fact]
    public void PackedSdk_ContainsAuthoringAssetsAndXmlDocumentation()
    {
        var root = RepositoryRoot();
        var package = Directory.EnumerateFiles(
                Path.Combine(root, "artifacts"),
                "CSweet.Agent.SDK.3.13.0.nupkg",
                SearchOption.TopDirectoryOnly)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
        if (package is null)
            return; // The release pack command is the gate that supplies the archive.

        using var archive = ZipFile.OpenRead(package);
        var entries = archive.Entries.Select(x => x.FullName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Contains("lib/net10.0/CSweet.Agent.SDK.xml", entries);
        Assert.Contains("docs/AGENT_AUTHORING.md", entries);
        Assert.Contains("schemas/csweet-plugin.v2.schema.json", entries);
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "CSweetAgentSdk.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root was not found.");
    }

    [GeneratedRegex(@"\[[^\]]+\]\((?<target>[^)]+)\)", RegexOptions.CultureInvariant)]
    private static partial Regex MarkdownLink();
}
