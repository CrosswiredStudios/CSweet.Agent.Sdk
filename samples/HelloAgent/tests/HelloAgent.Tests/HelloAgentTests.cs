using CSweet.Agent.SDK;
using HelloAgentSample;

namespace HelloAgentSample.Tests;

public sealed class HelloAgentTests
{
    [Fact]
    public async Task SaysHelloAndReportsProgress()
    {
        var runtime = new AgentTestRuntime();

        var result = await runtime.ExecuteCapabilityAsync(
            new HelloAgent(),
            HelloAgent.PrimaryCapability,
            new HelloRequest("C-Sweet"));

        Assert.True(result.Succeeded);
        Assert.Equal("Hello, C-Sweet!", result.Value!.Value.GetProperty("message").GetString());
        Assert.Single(runtime.Progress);
    }

    [Fact]
    public async Task ManifestMatchesAgent()
    {
        var root = RepositoryRoot();
        var manifest = await AgentManifestLoader.LoadAsync(
            Path.Combine(root, "csweet-plugin.json"),
            CancellationToken.None);
        var agent = new HelloAgent();

        Assert.Equal(agent.AgentId, manifest.Id);
        Assert.Equal(agent.Version, manifest.Version);
        Assert.Contains(HelloAgent.PrimaryCapability, manifest.Capabilities);
        Assert.True(File.Exists(Path.Combine(
            root,
            manifest.Runtime.ProjectPath!.Replace('/', Path.DirectorySeparatorChar))));
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "csweet-plugin.json")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Sample root was not found.");
    }
}
