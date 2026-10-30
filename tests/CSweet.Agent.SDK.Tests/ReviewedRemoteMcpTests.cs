using System.Text.Json;
using System.Text.Json.Nodes;
using CSweet.Agent.Contracts.Packaging;
using Xunit;
namespace CSweet.Agent.SDK.Tests;

public sealed class ReviewedRemoteMcpTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);
    private static AgentManifest Parse(string json) => JsonSerializer.Deserialize<AgentManifest>(json, Web)!;
    private static string Manifest => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "realtyleads-manifest.json"));
    private static JsonObject Node() => JsonNode.Parse(Manifest)!.AsObject();
    private static JsonObject Remote(JsonObject root) => root["reviewedRemoteMcp"]!.AsObject();
    private static void Rejected(JsonObject root) =>
        Assert.Throws<InvalidOperationException>(() => AgentManifestLoader.Validate(Parse(root.ToJsonString())));
    private static void Accepted(JsonObject root) => AgentManifestLoader.Validate(Parse(root.ToJsonString()));

    [Fact] public void RealtyLeadsProfileLoads() => AgentManifestLoader.Validate(Parse(Manifest));

    [Fact]
    public void AnyPublisherMayDeclareItsOwnEndpointHeaderAndTools()
    {
        var root = Node();
        root["id"] = "com.acme.plugin.orders";
        root["publisher"] = new JsonObject { ["id"] = "com.acme", ["name"] = "Acme" };
        var remote = Remote(root);
        remote["profile"] = "acme.orders.read.v1";
        remote["endpoint"] = "https://api.acme.example/v2/mcp";
        remote["header"] = "X-Acme-Token";
        remote["tools"]![0]!["remoteTool"] = "search_orders";
        remote["tools"]![1]!["remoteTool"] = "get_order";
        Accepted(root);
    }

    [Fact]
    public void BearerAuthenticationUsesAuthorization()
    {
        var root = Node();
        Remote(root)["authentication"] = "bearer";
        Remote(root)["header"] = "Authorization";
        Accepted(root);
        Remote(root)["header"] = "X-Api-Key";
        Rejected(root);
    }

    [Fact]
    public void EndpointHostIsLowercaseDnsHost() =>
        Assert.Equal("realtyleads.ai", ReviewedRemoteMcpValidator.EndpointHost("https://RealtyLeads.ai/mcp"));

    [Theory]
    [InlineData("http://realtyleads.ai/mcp")]
    [InlineData("https://realtyleads.ai")]
    [InlineData("https://realtyleads.ai/")]
    [InlineData("https://realtyleads.ai:8443/mcp")]
    [InlineData("https://realtyleads.ai:443/mcp")]
    [InlineData("https://user:pass@realtyleads.ai/mcp")]
    [InlineData("https://realtyleads.ai/mcp?key=1")]
    [InlineData("https://realtyleads.ai/mcp#x")]
    [InlineData("https://203.0.113.10/mcp")]
    [InlineData("https://[2001:db8::1]/mcp")]
    [InlineData("https://localhost/mcp")]
    [InlineData("https://intranet/mcp")]
    [InlineData("https://api.corp.internal/mcp")]
    [InlineData("https://printer.local/mcp")]
    [InlineData("https://bücher.example/mcp")]
    [InlineData("https://realtyleads.ai/m cp")]
    public void UnsafeEndpointsAreRejected(string endpoint)
    {
        var root = Node();
        Remote(root)["endpoint"] = endpoint;
        Rejected(root);
    }

    [Theory]
    [InlineData("Cookie")]
    [InlineData("Host")]
    [InlineData("Authorization")]
    [InlineData("X-")]
    [InlineData("X-Api Key")]
    public void ApiKeyHeadersMustBeCustomXHeaders(string header)
    {
        var root = Node();
        Remote(root)["header"] = header;
        Rejected(root);
    }

    [Theory]
    [InlineData("accountBinding", "shared")]
    [InlineData("transport", "streamable-http")]
    [InlineData("authentication", "oauth")]
    [InlineData("profile", "RealtyLeads.Read")]
    public void ClosedDeclarationFieldsAreEnforced(string field, string value)
    {
        var root = Node();
        Remote(root)[field] = value;
        Rejected(root);
    }

    [Fact]
    public void UnsupportedMcpProtocolRevisionIsRejected()
    {
        var root = Node();
        Remote(root)["protocolVersions"] = new JsonArray("2025-06-18");
        Rejected(root);
    }

    [Fact]
    public void RetiredClosedProfileReportsUpdateGuidance()
    {
        var root = Node();
        root["protocol"]!["minimumVersion"] = "2.5";
        var error = Assert.Throws<InvalidOperationException>(() => AgentManifestLoader.Validate(Parse(root.ToJsonString())));
        Assert.Contains("protocol 2.6", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ToolMappingMustCoverEveryCapabilityExactlyOnce()
    {
        var missing = Node();
        Remote(missing)["tools"]!.AsArray().RemoveAt(1);
        Rejected(missing);

        var duplicateRemote = Node();
        Remote(duplicateRemote)["tools"]![1]!["remoteTool"] = "search_inheritance_leads";
        Rejected(duplicateRemote);

        var unknownCapability = Node();
        Remote(unknownCapability)["tools"]![1]!["capability"] = "realtyleads.inheritance.delete.v1";
        Rejected(unknownCapability);

        var badName = Node();
        Remote(badName)["tools"]![0]!["remoteTool"] = "search leads";
        Rejected(badName);
    }

    [Theory]
    [InlineData("write")]
    [InlineData("destructive")]
    [InlineData("")]
    public void OnlyReadToolsAreAllowed(string effect)
    {
        var root = Node();
        Remote(root)["tools"]![0]!["effect"] = effect;
        Rejected(root);
    }

    [Fact]
    public void OpenOrUnboundedSchemasAreRejected()
    {
        var open = Node();
        open["provides"]![0]!["inputSchema"]!["additionalProperties"] = true;
        Rejected(open);

        var unboundedString = Node();
        unboundedString["provides"]![0]!["inputSchema"]!["properties"]!["state"]!.AsObject().Remove("maxLength");
        Rejected(unboundedString);

        var unboundedArray = Node();
        unboundedArray["provides"]![0]!["outputSchema"]!.AsObject().Remove("maxItems");
        Rejected(unboundedArray);

        var reference = Node();
        reference["provides"]![1]!["inputSchema"]!["properties"]!["id"] = new JsonObject { ["$ref"] = "#/defs/id" };
        Rejected(reference);

        var nullableRoot = Node();
        nullableRoot["provides"]![1]!["inputSchema"]!["type"] = new JsonArray("object", "null");
        Rejected(nullableRoot);
    }

    [Fact]
    public void WritesTimeoutsAndIdempotencyAreBounded()
    {
        var slow = Node();
        slow["provides"]![0]!["executionTimeoutSeconds"] = 31;
        Rejected(slow);

        var keyed = Node();
        keyed["provides"]![0]!["idempotency"] = "caller-key";
        Rejected(keyed);
    }

    [Theory]
    [InlineData("webAccess", "{\"mode\":\"Allowlist\",\"rules\":[]}")]
    [InlineData("requires", "[{\"name\":\"platform.llm.chat-stream.v1\"}]")]
    [InlineData("configuration", "[{\"key\":\"endpoint\",\"label\":\"Endpoint\",\"type\":\"string\"}]")]
    [InlineData("ui", "[{\"id\":\"panel\"}]")]
    public void NoOtherAuthorityMayBeDeclared(string section, string json)
    {
        var root = Node();
        root[section] = JsonNode.Parse(json);
        Assert.ThrowsAny<Exception>(() => AgentManifestLoader.Validate(Parse(root.ToJsonString())));
    }

    [Fact]
    public void KindAndRuntimeAreEnforced()
    {
        var kind = Node();
        kind["kind"] = "connector";
        Rejected(kind);

        var runtime = Node();
        runtime["runtime"]!["type"] = "dotnet-project";
        Rejected(runtime);
    }

    [Fact]
    public void OtherKindsMayNotUseReservedProtocolOrDeclaration()
    {
        var root = JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "realtyleads-manifest.json")))!.AsObject();
        root["kind"] = "agent";
        root["runtime"] = new JsonObject
        {
            ["type"] = "dotnet-project", ["projectPath"] = "src/A/A.csproj", ["targetFramework"] = "net10.0"
        };
        Assert.NotEmpty(ReviewedRemoteMcpValidator.Validate(Parse(root.ToJsonString())));
    }
}
