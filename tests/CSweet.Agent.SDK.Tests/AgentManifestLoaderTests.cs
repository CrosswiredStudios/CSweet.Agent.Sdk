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
              "manifestVersion": "2.0",
              "kind": "agent",
              "id": "com.example.agent",
              "name": "Example",
              "version": "1.0.0",
              "publisher": { "id": "example", "name": "Example" },
              "runtime": { "type": "dotnet-project", "projectPath": "src/Example.csproj", "targetFramework": "net10.0", "defaultActivationMode": "Manual" },
              "protocol": { "minimumVersion": "2.0", "maximumVersion": "2.x" },
              "provides": [],
              "requires": [],
              "events": { "subscribes": [] }
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
              "manifestVersion": "2.0",
              "kind": "agent",
              "id": "com.example.chief",
              "name": "Example Chief",
              "version": "1.0.0",
              "publisher": { "id": "example", "name": "Example" },
              "runtime": { "type": "dotnet-project", "projectPath": "src/Example.csproj", "targetFramework": "net10.0", "defaultActivationMode": "Manual" },
              "protocol": { "minimumVersion": "2.0", "maximumVersion": "2.x" },
              "provides": [{ "name": "management.check-in.v1", "description": "Check in.", "inputSchema": { "type": "object" }, "outputSchema": { "type": "object" }, "executionTimeoutSeconds": 30, "idempotency": "work-item" }],
              "requires": [{ "name": "platform.business-profile.read.v1", "scope": "organization" }],
              "events": { "subscribes": ["review.due.v1"] }
            }
            """);

            var manifest = await AgentManifestLoader.LoadAsync(path, CancellationToken.None);

            Assert.Contains("management.check-in.v1", manifest.Capabilities);
            Assert.Contains(PlatformCapabilities.BusinessProfileRead, manifest.RequestedCapabilities);
            Assert.Contains("review.due.v1", manifest.RequestedSubscriptions);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task LoadAsync_AcceptsCustomProviderCapabilities()
    {
        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(path, """
            {
              "manifestVersion": "2.0",
              "kind": "agent",
              "id": "com.example.unknown-grant",
              "name": "Unknown Grant",
              "version": "1.0.0",
              "publisher": { "id": "example", "name": "Example" },
              "runtime": { "type": "dotnet-project", "projectPath": "src/Example.csproj", "targetFramework": "net10.0", "defaultActivationMode": "Manual" },
              "protocol": { "minimumVersion": "2.0", "maximumVersion": "2.x" },
              "provides": [{ "name": "example.unregistered.v1", "description": "Unknown.", "inputSchema": { "type": "object" }, "outputSchema": { "type": "object" }, "executionTimeoutSeconds": 30, "idempotency": "work-item" }],
              "requires": [],
              "events": { "subscribes": [] }
            }
            """);

            var manifest = await AgentManifestLoader.LoadAsync(path, CancellationToken.None);

            Assert.Contains("example.unregistered.v1", manifest.Capabilities);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task LoadAsync_AcceptsDeclarativeProgressiveOAuthSetup()
    {
        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(path, """
            {
              "manifestVersion": "2.0", "kind": "agent", "id": "com.example.connected", "name": "Connected",
              "version": "1.0.0", "publisher": { "id": "example", "name": "Example" },
              "runtime": { "type": "dotnet-project", "projectPath": "src/Example.csproj", "targetFramework": "net10.0", "defaultActivationMode": "Manual", "supportsMultipleInstallations": true, "maximumConcurrentJobs": 1 },
              "protocol": { "minimumVersion": "2.0", "maximumVersion": "2.x" },
              "provides": [{ "name": "example.setup.validate.v1", "description": "Validate setup.", "inputSchema": { "type": "object" }, "outputSchema": { "type": "object" }, "executionTimeoutSeconds": 30, "idempotency": "caller-key" }],
              "requires": [], "events": { "subscribes": [] }, "configuration": [], "credentials": [],
              "connections": [{
                "id": "provider", "type": "oauth2", "providerProfile": "com.example.provider",
                "allowedOrigins": ["https://api.example.com"],
                "scopeSets": [{ "id": "base", "label": "Read account", "purpose": "Discover the account.", "required": true, "scopes": ["account.read"] }]
              }],
              "setup": { "required": true, "entryFlow": "onboarding", "flows": [{ "id": "onboarding", "title": "Connect", "steps": [
                { "id": "permissions", "kind": "permission-summary", "title": "Review access" },
                { "id": "connect", "kind": "oauth-connect", "title": "Connect", "connection": "provider", "scopeSet": "base" },
                { "id": "validate", "kind": "health-check", "title": "Validate", "capability": "example.setup.validate.v1" }
              ] }] },
              "webAccess": { "mode": "None", "rules": [] },
              "ui": [{ "kind": "personal-settings", "id": "settings", "title": "Connection", "flow": "onboarding" }]
            }
            """);

            var manifest = await AgentManifestLoader.LoadAsync(path, CancellationToken.None);

            Assert.Equal("com.example.provider", Assert.Single(manifest.Connections).ProviderProfile);
            Assert.Equal("onboarding", manifest.Setup?.EntryFlow);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData("html")]
    [InlineData("javascript")]
    [InlineData("iframe")]
    [InlineData("razor")]
    public async Task LoadAsync_RejectsExecutableSetupUi(string kind)
    {
        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(path, $$"""
            {
              "manifestVersion": "2.0", "kind": "agent", "id": "com.example.unsafe", "name": "Unsafe", "version": "1.0.0",
              "publisher": { "id": "example", "name": "Example" },
              "runtime": { "type": "dotnet-project", "projectPath": "src/Example.csproj", "targetFramework": "net10.0", "defaultActivationMode": "Manual", "supportsMultipleInstallations": true, "maximumConcurrentJobs": 1 },
              "protocol": { "minimumVersion": "2.0", "maximumVersion": "2.x" }, "provides": [], "requires": [], "events": { "subscribes": [] },
              "configuration": [], "credentials": [], "connections": [],
              "setup": { "required": true, "entryFlow": "onboarding", "flows": [{ "id": "onboarding", "title": "Setup", "steps": [{ "id": "unsafe", "kind": "{{kind}}", "title": "Unsafe" }] }] },
              "webAccess": { "mode": "None", "rules": [] }, "ui": []
            }
            """);

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => AgentManifestLoader.LoadAsync(path, CancellationToken.None));
            Assert.Contains("unsafe or unsupported", exception.Message);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(86401)]
    public async Task LoadAsync_RejectsOutOfRangeTimeout(int timeout)
    {
        var path = await WriteManifestAsync(timeout: timeout);
        try
        {
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => AgentManifestLoader.LoadAsync(path, CancellationToken.None));
            Assert.Contains("between 1 and 86400", exception.Message);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task LoadAsync_RejectsDuplicateCapabilities()
    {
        var path = await WriteManifestAsync(duplicateProvide: true);
        try
        {
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => AgentManifestLoader.LoadAsync(path, CancellationToken.None));
            Assert.Contains("must not contain duplicate names", exception.Message);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task LoadAsync_RejectsIncorrectDescriptorHash()
    {
        var path = await WriteManifestAsync(descriptorHash: "not-the-canonical-hash");
        try
        {
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => AgentManifestLoader.LoadAsync(path, CancellationToken.None));
            Assert.Contains("descriptorHash does not match", exception.Message);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData("required")]
    [InlineData("sometimes")]
    public async Task LoadAsync_RejectsUnsupportedIdempotency(string idempotency)
    {
        var path = await WriteManifestAsync(idempotency: idempotency);
        try
        {
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => AgentManifestLoader.LoadAsync(path, CancellationToken.None));
            Assert.Contains("idempotency is unsupported", exception.Message);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData("../Agent.csproj")]
    [InlineData("/src/Agent.csproj")]
    [InlineData("src/Agent.txt")]
    public async Task LoadAsync_RejectsUnsafeProjectPath(string projectPath)
    {
        var path = await WriteManifestAsync(projectPath: projectPath);
        try
        {
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => AgentManifestLoader.LoadAsync(path, CancellationToken.None));
            Assert.Contains("runtime.projectPath", exception.Message);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData("1.0", "2.x")]
    [InlineData("2.0", "3.x")]
    public async Task LoadAsync_RejectsInvalidProtocolRange(string minimum, string maximum)
    {
        var path = await WriteManifestAsync(minimum: minimum, maximum: maximum);
        try
        {
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => AgentManifestLoader.LoadAsync(path, CancellationToken.None));
            Assert.Contains("protocol 2.0 through 2.x", exception.Message);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static async Task<string> WriteManifestAsync(
        int timeout = 30,
        string idempotency = "work-item",
        string projectPath = "src/Example/Example.csproj",
        string minimum = "2.0",
        string maximum = "2.x",
        bool duplicateProvide = false,
        string? descriptorHash = null)
    {
        var path = Path.GetTempFileName();
        var capability = $$"""
          { "name": "example.custom.v1", "description": "Custom.", "inputSchema": { "type": "object" }, "outputSchema": { "type": "object" }, "executionTimeoutSeconds": {{timeout}}, "idempotency": "{{idempotency}}"{{(descriptorHash is null ? string.Empty : ", \"descriptorHash\": \"" + descriptorHash + "\"")}} }
        """;
        await File.WriteAllTextAsync(path, $$"""
        {
          "manifestVersion": "2.0",
          "kind": "agent",
          "id": "com.example.agent",
          "name": "Example",
          "version": "1.0.0",
          "publisher": { "id": "example", "name": "Example" },
          "runtime": {
            "type": "dotnet-project",
            "projectPath": "{{projectPath.Replace("\\", "\\\\")}}",
            "targetFramework": "net10.0",
            "defaultActivationMode": "Manual",
            "supportsMultipleInstallations": true,
            "maximumConcurrentJobs": 1
          },
          "protocol": { "minimumVersion": "{{minimum}}", "maximumVersion": "{{maximum}}" },
          "provides": [{{capability}}{{(duplicateProvide ? "," + capability : string.Empty)}}],
          "requires": [{ "name": "provider.custom.read.v1", "scope": "organization", "purpose": "Read bound provider data" }],
          "events": { "subscribes": [] },
          "configuration": [],
          "credentials": [],
          "webAccess": { "mode": "None", "rules": [] },
          "ui": []
        }
        """);
        return path;
    }
}
