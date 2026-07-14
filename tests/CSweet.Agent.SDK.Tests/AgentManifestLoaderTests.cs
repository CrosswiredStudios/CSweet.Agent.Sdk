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
}
