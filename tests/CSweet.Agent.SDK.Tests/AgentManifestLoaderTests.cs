using CSweet.Agent.SDK;

namespace CSweet.Agent.SDK.Tests;

public sealed class AgentManifestLoaderTests
{
    [Fact]
    public async Task LoadAsync_ReadsDotNetProjectManifest()
    {
        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(path, """
            {
              "manifestVersion": "1.0",
              "id": "com.example.agent",
              "name": "Example",
              "version": "1.0.0",
              "publisher": { "id": "example", "name": "Example" },
              "runtime": { "type": "dotnet-project", "projectPath": "src/Example.csproj" },
              "protocol": { "minimumVersion": "1.0", "maximumVersion": "1.x" }
            }
            """);

            var manifest = await AgentManifestLoader.LoadAsync(path, CancellationToken.None);

            Assert.Equal("com.example.agent", manifest.Id);
            Assert.Equal("src/Example.csproj", manifest.Runtime.ProjectPath);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task LoadAsync_MapsCanonicalProvidesRequiresAndEvents()
    {
        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(path, """
            {
              "manifestVersion": "1.0",
              "kind": "agent",
              "id": "com.example.chief",
              "name": "Example Chief",
              "version": "1.0.0",
              "publisher": { "id": "example", "name": "Example" },
              "runtime": { "type": "dotnet-project", "projectPath": "src/Example.csproj" },
              "protocol": { "minimumVersion": "1.0", "maximumVersion": "1.x" },
              "provides": [{ "name": "management.check-in.v1" }],
              "requires": [{ "name": "platform.business-profile.read.v1", "scope": "organization" }],
              "events": { "subscribes": ["review.due.v1"], "publishes": ["status.reported.v1"] }
            }
            """);

            var manifest = await AgentManifestLoader.LoadAsync(path, CancellationToken.None);

            Assert.Contains("management.check-in.v1", manifest.Capabilities);
            Assert.Contains(PlatformCapabilities.BusinessProfileRead, manifest.RequestedCapabilities);
            Assert.Contains("review.due.v1", manifest.RequestedSubscriptions);
            Assert.Contains("status.reported.v1", manifest.RequestedPublications);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task LoadAsync_RejectsCapabilityMissingFromSdkCatalog()
    {
        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(path, """
            {
              "manifestVersion": "1.0",
              "kind": "agent",
              "id": "com.example.unknown-grant",
              "name": "Unknown Grant",
              "version": "1.0.0",
              "publisher": { "id": "example", "name": "Example" },
              "runtime": { "type": "dotnet-project", "projectPath": "src/Example.csproj" },
              "protocol": { "minimumVersion": "1.0", "maximumVersion": "1.x" },
              "provides": [{ "name": "example.unregistered.v1" }],
              "requires": [],
              "events": { "subscribes": [], "publishes": [] }
            }
            """);

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => AgentManifestLoader.LoadAsync(path, CancellationToken.None));

            Assert.Contains("example.unregistered.v1", exception.Message);
            Assert.Contains("not registered in CSweet.Agent.SDK", exception.Message);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
