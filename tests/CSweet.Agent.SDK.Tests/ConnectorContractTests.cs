using System.Text.Json;
using System.Text.Json.Nodes;
using CSweet.Agent.Contracts.Packaging;

namespace CSweet.Agent.SDK.Tests;

public sealed class ConnectorContractTests
{
    [Theory]
    [InlineData("2.1", "/items/*/owner", false)]
    [InlineData("2.2", "/items/*/owner", true)]
    [InlineData("2.2", "/account/id", true)]
    [InlineData("2.2", "$..owner", false)]
    [InlineData("2.2", "/items/*/children/*/owner", false)]
    [InlineData("2.2", "/items/*/owner\n", false)]
    [InlineData("2.2", "/items/*/eval()", false)]
    public void ResponseResourceBindingsRequireNewProtocolAndBoundedPointers(string protocol, string pointer, bool valid)
    {
        var node = Manifest(); node["protocol"]!["minimumVersion"] = protocol;
        Http(node)["responseResourcePointers"] = new JsonArray(pointer);
        Assert.Equal(valid, Validate(node).Count == 0);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ResponseBindingsRejectBootstrapAndDuplicateDeclarations(bool bootstrap)
    {
        var node = Manifest(); node["protocol"]!["minimumVersion"] = "2.2";
        Http(node)["bootstrap"] = bootstrap;
        Http(node)["responseResourcePointers"] = bootstrap ? new JsonArray("/owner") : new JsonArray("/owner", "/owner");
        Assert.NotEmpty(Validate(node));
    }

    [Theory]
    [InlineData("resumable-range.v1", "/mediaAssetId", true)]
    [InlineData("resumable-range.v1", null, false)]
    [InlineData("execute-plugin-code", "/mediaAssetId", false)]
    [InlineData("https://external.example/handler", "/mediaAssetId", false)]
    public void MediaProtocolIsAReviewedFixedHostProtocol(string protocol, string? pointer, bool valid)
    {
        var node = Manifest(); var operation = node["providerOperations"]![0]!;
        operation["effect"] = "write"; operation["idempotency"] = "caller-key";
        node["provides"]![0]!["idempotency"] = "caller-key";
        Http(node)["method"] = "POST"; Http(node)["mediaInput"] = pointer; Http(node)["mediaProtocol"] = protocol;
        Assert.Equal(valid, Validate(node).Count == 0);
    }

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

    [Theory]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    [InlineData("DELETE")]
    public void ConditionalMutationsRequireProtocol23AndExactRequiredString(string method)
    {
        var node = ConditionalManifest(); Http(node)["method"] = method;
        Assert.Empty(Validate(node));
        node["protocol"]!["minimumVersion"] = "2.2";
        Assert.Contains(Validate(node), x => x.Contains("If-Match"));
    }

    [Theory]
    [InlineData("GET", false, null)]
    [InlineData("POST", false, null)]
    [InlineData("PUT", true, null)]
    [InlineData("PUT", false, "/media")]
    public void ConditionalHeadersCannotExtendReadBootstrapOrMediaAuthority(string method, bool bootstrap, string? media)
    {
        var node = ConditionalManifest(); Http(node)["method"] = method;
        Http(node)["bootstrap"] = bootstrap; Http(node)["mediaInput"] = media;
        Assert.Contains(Validate(node), x => x.Contains("If-Match"));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"type\":\"string\"}")]
    [InlineData("{\"type\":\"string\",\"maxLength\":257}")]
    [InlineData("{\"type\":\"string\",\"maxLength\":\"256\"}")]
    [InlineData("{\"type\":\"string\",\"maxLength\":null}")]
    [InlineData("{\"type\":[\"string\",\"null\"],\"maxLength\":256}")]
    public void ConditionalInputSchemaFailsClosed(string field)
    {
        var node = ConditionalManifest();
        foreach (var schema in new[] { node["provides"]![0]!["inputSchema"]!, node["providerOperations"]![0]!["inputSchema"]! })
            schema["properties"]!["etag"] = JsonNode.Parse(field);
        Assert.Contains(Validate(node), x => x.Contains("If-Match"));
    }

    [Fact]
    public void OptionalOrUndeclaredConditionalInputFailsClosed()
    {
        var node = ConditionalManifest(); Http(node)["ifMatchInput"] = "/unknown";
        Assert.Contains(Validate(node), x => x.Contains("If-Match"));
        Http(node)["ifMatchInput"] = "/etag";
        foreach (var schema in new[] { node["provides"]![0]!["inputSchema"]!, node["providerOperations"]![0]!["inputSchema"]! })
            schema["required"] = new JsonArray();
        Assert.Contains(Validate(node), x => x.Contains("If-Match"));
    }

    [Theory]
    [InlineData("\"version-1\"")]
    [InlineData("\"*\"")]
    public void SingleStrongEntityTagIsPreserved(string tag) => Assert.Equal(tag, ConnectorEntityTag.RequireStrong(tag));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("*")]
    [InlineData("\"\"")]
    [InlineData("W/\"version\"")]
    [InlineData("\"a\", \"b\"")]
    [InlineData("\"a b\"")]
    [InlineData("\"a\r\nb\"")]
    [InlineData("\"é\"")]
    public void UnsafeEntityTagsAreRejected(string? tag) => Assert.Throws<ArgumentException>(() => ConnectorEntityTag.RequireStrong(tag!));

    [Fact]
    public void EntityTagLengthIsBounded()
    {
        Assert.Equal(256, ConnectorEntityTag.RequireStrong("\"" + new string('a', 254) + "\"").Length);
        Assert.Throws<ArgumentException>(() => ConnectorEntityTag.RequireStrong("\"" + new string('a', 255) + "\""));
    }

    private static JsonNode ConditionalManifest()
    {
        var node = Manifest(); node["protocol"]!["minimumVersion"] = "2.3";
        node["providerOperations"]![0]!["effect"] = "write";
        node["providerOperations"]![0]!["idempotency"] = "caller-key";
        node["provides"]![0]!["idempotency"] = "caller-key";
        var schema = JsonNode.Parse("""{"type":"object","additionalProperties":false,"required":["etag"],"properties":{"etag":{"type":"string","maxLength":256}}}""")!;
        node["provides"]![0]!["inputSchema"] = schema.DeepClone();
        node["providerOperations"]![0]!["inputSchema"] = schema;
        Http(node)["method"] = "PUT"; Http(node)["ifMatchInput"] = "/etag";
        return node;
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
