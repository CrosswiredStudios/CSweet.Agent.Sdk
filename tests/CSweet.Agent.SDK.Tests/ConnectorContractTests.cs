using System.Text.Json;
using System.Text.Json.Nodes;
using CSweet.Agent.Contracts.Packaging;

namespace CSweet.Agent.SDK.Tests;

public sealed class ConnectorContractTests
{
    [Fact]
    public void SecondProviderNeedsNoProviderSpecificSdkCode() => Assert.Empty(Validate(Manifest()));

    [Theory]
    [InlineData("/items", true)]
    [InlineData("$.items", false)]
    [InlineData("/items/*/eval()", false)]
    public void AccountProjectionUsesOnlyBoundedPointers(string pointer, bool valid)
    {
        var node = Manifest(); Http(node)["bootstrap"] = true;
        node["setup"] = new JsonObject { ["required"] = true, ["entryFlow"] = "connect", ["flows"] = new JsonArray(
            new JsonObject { ["id"] = "connect", ["steps"] = new JsonArray(new JsonObject
            {
                ["id"] = "account", ["kind"] = "account-selector", ["connection"] = "account", ["capability"] = "example.api.read.v1",
                ["accountOptions"] = new JsonObject { ["itemsPointer"] = pointer, ["idPointer"] = "/id", ["namePointer"] = "/label" }
            }) }) };
        Assert.Equal(valid, Validate(node).Count == 0);
    }

    [Fact]
    public void LiteralResourcePrefixRequiresAHostBoundField()
    {
        var node = Manifest(); Http(node)["boundResourceQueryPrefix"] = "account==";
        Assert.NotEmpty(Validate(node));
        Http(node)["boundResourceQuery"] = "ids";
        Assert.Empty(Validate(node));
    }

    [Fact]
    public void BootstrapDoesNotExposeSecretExtraction()
    {
        var node = Manifest(); Http(node)["bootstrap"] = true;
        Http(node)["secretResponseFields"] = new JsonArray("/secret");
        Assert.NotEmpty(Validate(node));
    }

    [Theory]
    [InlineData("conversation.v1", "2.1", true)]
    [InlineData("arbitrary-code.v1", "2.1", false)]
    [InlineData("conversation.v1", "2.0", false)]
    public void SetupAssistanceUsesOnlyTheProtocol21FixedProfile(string profile, string minimumVersion, bool valid)
    {
        var node = Manifest(); node["kind"] = "agent";
        node["connections"] = new JsonArray(); node["provides"] = new JsonArray(); node["providerOperations"] = new JsonArray();
        node["protocol"]!["minimumVersion"] = minimumVersion;
        node["setup"] = new JsonObject { ["required"] = true, ["assistance"] = new JsonObject { ["profile"] = profile } };
        Assert.Equal(valid, Validate(node).Count == 0);
    }

    [Theory]
    [InlineData("http://api.example.com/items")]
    [InlineData("https://127.0.0.1/items")]
    [InlineData("https://other.example.com/items")]
    [InlineData("https://api.example.com/items?token=x")]
    [InlineData("https://api.example.com:8443/items")]
    [InlineData("https://user:pass@api.example.com/items")]
    [InlineData("https://api.example.com/items#redirect")]
    public void UnsafeOrUndeclaredDestinationsAreRejected(string endpoint)
    {
        var node = Manifest(); Http(node)["endpoint"] = endpoint;
        Assert.NotEmpty(Validate(node));
    }

    [Theory]
    [InlineData("state")]
    [InlineData("redirect_uri")]
    [InlineData("code_challenge_method")]
    [InlineData("request_uri")]
    public void OAuthExtensionsCannotReplaceSecurityFields(string field)
    {
        var node = Manifest();
        node["connections"]![0]!["provider"]!["authorizationParameters"] = new JsonObject { [field] = "attacker" };
        Assert.NotEmpty(Validate(node));
    }

    [Fact]
    public void DuplicateConnectionsReturnErrorsRatherThanThrowing()
    {
        var node = Manifest(); var connections = node["connections"]!.AsArray();
        connections.Add(connections[0]!.DeepClone());
        Assert.NotEmpty(Validate(node));
    }

    [Fact]
    public void BootstrapCannotMutate()
    {
        var node = Manifest(); Http(node)["bootstrap"] = true; Http(node)["method"] = "POST";
        Assert.NotEmpty(Validate(node));
    }

    [Fact]
    public void MappingCannotReplaceFixedQueryValues()
    {
        var node = Manifest(); Http(node)["queryInputs"] = new JsonObject { ["mine"] = "/otherAccount" };
        Assert.NotEmpty(Validate(node));
    }

    [Fact]
    public void ToolAndOperationSchemasMustMatch()
    {
        var node = Manifest(); node["providerOperations"]![0]!["inputSchema"]!["properties"]!["extra"] = new JsonObject { ["type"] = "string" };
        Assert.NotEmpty(Validate(node));
    }

    [Fact]
    public void NewFeaturesCannotLoadOnProtocol20()
    {
        var node = Manifest(); node["protocol"]!["minimumVersion"] = "2.0";
        Assert.NotEmpty(Validate(node));
    }

    [Fact]
    public void ConnectorCannotRequestLlmAuthority()
    {
        var node = Manifest(); node["requires"] = JsonNode.Parse("""[{"name":"platform.llm.chat.v1","scope":"organization","purpose":"not allowed"}]""");
        Assert.NotEmpty(Validate(node));
    }

    [Fact]
    public void DependencyConsumerCannotAlsoHoldCredentials()
    {
        var node = Manifest(); node["kind"] = "agent"; node["connections"] = new JsonArray(); node["provides"] = new JsonArray(); node["providerOperations"] = new JsonArray();
        node["dependencies"] = JsonNode.Parse("""[{"id":"accounts","pluginId":"com.example.accounts","publisherId":"com.example","minimumVersion":"0.1.0","maximumVersionExclusive":"0.2.0"}]""");
        node["credentials"] = JsonNode.Parse("""[{"name":"token","type":"bearer"}]""");
        Assert.Contains(Validate(node), x => x.Contains("raw network authority"));
    }

    private static JsonNode Http(JsonNode node) => node["providerOperations"]![0]!["http"]!;
    private static IReadOnlyList<string> Validate(JsonNode node) => ConnectorContractValidator.Validate(
        node.Deserialize<AgentManifest>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!);
    private static JsonNode Manifest() => JsonNode.Parse("""
        {
          "kind":"connector", "id":"com.example.connector", "name":"Example", "version":"0.1.0",
          "publisher":{"id":"com.example","name":"Example"},
          "runtime":{"supportsMultipleInstallations":true},
          "protocol":{"minimumVersion":"2.1","maximumVersion":"2.x"},
          "provides":[{"name":"example.api.read.v1","description":"Read account","executionTimeoutSeconds":30,"idempotency":"none",
            "inputSchema":{"type":"object","properties":{},"additionalProperties":false},"outputSchema":{"type":"object"}}],
          "connections":[{"id":"account","providerProfile":"example.account","allowedOrigins":["https://api.example.com"],
            "scopeSets":[{"id":"base","scopes":["account.read"]}],
            "provider":{"displayName":"Example","authorizationEndpoint":"https://identity.example.com/authorize",
              "tokenEndpoint":"https://identity.example.com/token","revocationEndpoint":"https://identity.example.com/revoke"}}],
          "providerOperations":[{"capability":"example.api.read.v1","effect":"read","idempotency":"none",
            "inputSchema":{"type":"object","properties":{},"additionalProperties":false},"outputSchema":{"type":"object"},
            "http":{"connection":"account","scopeSets":["base"],"method":"GET","endpoint":"https://api.example.com/items","queryConstants":{"mine":"true"}}}]
        }
        """)!;
}
