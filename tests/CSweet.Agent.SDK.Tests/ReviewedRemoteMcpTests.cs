using System.Text.Json;
using CSweet.Agent.Contracts.Packaging;
using Xunit;
namespace CSweet.Agent.SDK.Tests;
public sealed class ReviewedRemoteMcpTests
{
    private static AgentManifest Parse(string json) => JsonSerializer.Deserialize<AgentManifest>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
    private static string Manifest => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "realtyleads-manifest.json"));
    [Fact] public void ReviewedProfileLoads() => AgentManifestLoader.Validate(Parse(Manifest));
    [Theory]
    [InlineData("https://realtyleads.ai/mcp", "https://realtyleads.ai/mcp/other")]
    [InlineData("X-Api-Key", "Authorization")]
    [InlineData("owner-business-installation.v1", "shared")]
    [InlineData("\"minimumVersion\": \"2.5\"", "\"minimumVersion\": \"2.4\"")]
    [InlineData("stateless-post", "streamable-http")]
    [InlineData("\"kind\": \"remote-mcp\"", "\"kind\": \"connector\"")]
    public void UnreviewedAuthorityIsRejected(string from, string to) => Assert.Throws<InvalidOperationException>(() => AgentManifestLoader.Validate(Parse(Manifest.Replace(from, to))));
}
