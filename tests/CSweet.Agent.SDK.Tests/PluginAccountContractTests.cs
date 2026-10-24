using System.Text.Json;
using System.Text.Json.Nodes;
using CSweet.Agent.Contracts.Packaging;

namespace CSweet.Agent.SDK.Tests;

public sealed class PluginAccountContractTests
{
    [Fact]
    public void EnterpriseAccountsCanUseOneInstallationAndProviderNeutralLabels() => Assert.Empty(Validate(Manifest()));

    [Fact]
    public void InstallationDoesNotRunAnAccountConsentFlowOrAcceptMissingConsentSteps()
    {
        var node = Manifest(); node["setup"]!["required"] = true;
        Assert.NotEmpty(Validate(node));
        node = Manifest(); node["setup"]!["flows"]![0]!["steps"] = new JsonArray();
        Assert.NotEmpty(Validate(node));
    }

    [Theory]
    [InlineData("2.0")]
    [InlineData("2.1")]
    [InlineData("2.2")]
    [InlineData("2.3")]
    public void EarlierHostsMustRejectEnterpriseSemantics(string minimum)
    {
        var node = Manifest(); node["protocol"]!["minimumVersion"] = minimum;
        Assert.Contains(Validate(node), x => x.Contains("protocol 2.4"));
    }

    [Theory]
    [InlineData("agent")]
    [InlineData("service")]
    public void AccountsAreNotAgentOrServiceAuthority(string kind)
    {
        var node = Manifest(); node["kind"] = kind;
        Assert.NotEmpty(Validate(node));
    }

    [Theory]
    [InlineData("layout", "https://remote.example.com/ui")]
    [InlineData("connection", "undeclared")]
    [InlineData("setupFlow", "undeclared")]
    [InlineData("discoveryCapability", "undeclared.api.read.v1")]
    [InlineData("privacyPolicyUrl", "http://example.com/privacy")]
    [InlineData("termsOfServiceUrl", "https://localhost/terms")]
    [InlineData("resourceType", "channels\n")]
    [InlineData("consentPurpose", "")]
    public void InvalidDeclarationsFailClosed(string field, string value)
    {
        var node = Manifest(); node["accounts"]![field] = value;
        Assert.NotEmpty(Validate(node));
    }

    [Theory]
    [InlineData("javascript")]
    [InlineData("html")]
    [InlineData("iframe")]
    [InlineData("redirectUri")]
    public void ExecutableAndRedirectExtensionsAreNotAnEscapeHatch(string field)
    {
        var node = Manifest(); node["accounts"]![field] = "https://attacker.example.com";
        Assert.Throws<JsonException>(() => Deserialize(node));
    }

    [Fact]
    public void DiscoveryAndIdentityMustUseAuthenticatedBootstrapReads()
    {
        var node = Manifest(); node["providerOperations"]![1]!["http"]!["bootstrap"] = false;
        Assert.Contains(Validate(node), x => x.Contains("Resource discovery"));
        node = Manifest(); node["accounts"]!["identity"] = new JsonObject { ["capability"] = "example.api.read.v1" };
        Assert.Contains(Validate(node), x => x.Contains("Identity"));
    }

    [Fact]
    public void PermissionsCannotExposeBootstrapOrUndeclaredOperations()
    {
        foreach (var capability in new[] { "example.api.discover.v1", "other.api.write.v1" })
        {
            var node = Manifest(); node["accounts"]!["permissions"]![0]!["capabilities"] = new JsonArray(capability);
            Assert.Contains(Validate(node), x => x.Contains("business permission"));
        }
    }

    [Fact]
    public void PermissionMustCoverActualOperationScopes()
    {
        var node = Manifest(); node["accounts"]!["permissions"]![0]!["scopeSets"] = new JsonArray();
        Assert.NotEmpty(Validate(node));
    }

    [Fact]
    public void DuplicateConnectionsAndPermissionsProduceValidationErrors()
    {
        var node = Manifest(); node["connections"]!.AsArray().Add(node["connections"]![0]!.DeepClone());
        Assert.NotEmpty(Validate(node));
        node = Manifest(); node["accounts"]!["permissions"]!.AsArray().Add(node["accounts"]!["permissions"]![0]!.DeepClone());
        Assert.NotEmpty(Validate(node));
    }

    [Theory]
    [InlineData("{\"type\":\"string\",\"maxLength\":2048}", true)]
    [InlineData("{\"type\":\"string\"}", false)]
    [InlineData("{\"type\":\"string\",\"maxLength\":2049}", false)]
    [InlineData("{\"type\":[],\"maxLength\":20}", false)]
    [InlineData("{\"type\":\"string\",\"maxLength\":\"20\"}", false)]
    public void PaginationMustHaveBoundedOptionalMappedInput(string pageSchema, bool valid)
    {
        var node = Paginated();
        foreach (var schema in new[] { node["providerOperations"]![1]!["inputSchema"]!, node["provides"]![1]!["inputSchema"]! })
            schema["properties"]!["pageToken"] = JsonNode.Parse(pageSchema);
        Assert.Equal(valid, Validate(node).Count == 0);
    }

    [Fact]
    public void PaginationRejectsRequiredOrUnmappedTokensAndUnpairedContinuation()
    {
        var node = Paginated();
        foreach (var schema in new[] { node["providerOperations"]![1]!["inputSchema"]!, node["provides"]![1]!["inputSchema"]! })
            schema["required"] = new JsonArray("pageToken");
        Assert.NotEmpty(Validate(node));
        node = Paginated(); node["providerOperations"]![1]!["http"]!["queryInputs"] = new JsonObject();
        Assert.NotEmpty(Validate(node));
        node = Paginated(); node["accounts"]!.AsObject().Remove("pageTokenInput");
        Assert.NotEmpty(Validate(node));
    }

    [Fact]
    public void LifecycleIntervalsAreGenericButDeletionTargetCannotExceedDeadline()
    {
        var node = Manifest(); node["accounts"]!["lifecycle"] = JsonNode.Parse("""{"authorizationCheckHours":48,"metadataRefreshDays":90,"deletionMaximumDays":14,"deletionTargetHours":48,"revocationScope":"subject-project"}""");
        Assert.Empty(Validate(node));
        node["accounts"]!["lifecycle"]!["deletionTargetHours"] = 337;
        Assert.NotEmpty(Validate(node));
    }

    [Fact]
    public void Protocol24PreservesConditionalMutationsAndResponseOwnership()
    {
        var node = Manifest(); var operation = node["providerOperations"]![0]!;
        operation["effect"] = "write"; operation["idempotency"] = "caller-key";
        node["provides"]![0]!["idempotency"] = "caller-key";
        operation["http"]!["method"] = "PUT"; operation["http"]!["ifMatchInput"] = "/etag";
        operation["http"]!["responseResourcePointers"] = new JsonArray("/owner");
        var schema = JsonNode.Parse("""{"type":"object","additionalProperties":false,"required":["etag"],"properties":{"etag":{"type":"string","maxLength":256}}}""")!;
        operation["inputSchema"] = schema.DeepClone(); node["provides"]![0]!["inputSchema"] = schema;
        Assert.Empty(Validate(node));
    }

    private static AgentManifest Deserialize(JsonNode node) => node.Deserialize<AgentManifest>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
    private static IReadOnlyList<string> Validate(JsonNode node) => ConnectorContractValidator.Validate(Deserialize(node));
    private static JsonNode Paginated()
    {
        var node = Manifest(); node["accounts"]!["pageTokenInput"] = "pageToken";
        node["accounts"]!["resourceProjection"]!["nextPageTokenPointer"] = "/nextPageToken";
        node["providerOperations"]![1]!["http"]!["queryInputs"] = new JsonObject { ["pageToken"] = "/pageToken" };
        var schema = JsonNode.Parse("""{"type":"object","additionalProperties":false,"properties":{"pageToken":{"type":"string","maxLength":2048}}}""")!;
        node["providerOperations"]![1]!["inputSchema"] = schema.DeepClone(); node["provides"]![1]!["inputSchema"] = schema;
        return node;
    }
    private static JsonNode Manifest()
    {
        var node = ConnectorContractTests.Manifest(); node["protocol"]!["minimumVersion"] = "2.4";
        node["runtime"]!["supportsMultipleInstallations"] = false;
        var discovery = node["providerOperations"]![0]!.DeepClone(); discovery["capability"] = "example.api.discover.v1";
        discovery["http"]!["bootstrap"] = true;
        node["providerOperations"]!.AsArray().Add(discovery);
        var provided = node["provides"]![0]!.DeepClone(); provided["name"] = "example.api.discover.v1";
        node["provides"]!.AsArray().Add(provided);
        node["setup"] = JsonNode.Parse("""{"required":false,"entryFlow":"connect","flows":[{"id":"connect","title":"Connect","steps":[{"id":"consent","kind":"oauth-connect","connection":"account","scopeSet":"base"}]}]}""");
        node["accounts"] = JsonNode.Parse("""
          {"connection":"account","setupFlow":"connect","accountLabel":"Account","accountsLabel":"Accounts",
           "resourceType":"example.workspace","resourceLabel":"Workspace","resourcesLabel":"Workspaces",
           "discoveryCapability":"example.api.discover.v1","resourceProjection":{"itemsPointer":"/items","idPointer":"/id","namePointer":"/name"},
           "privacyPolicyUrl":"https://example.com/privacy","termsOfServiceUrl":"https://example.com/terms",
           "policyVersion":"2026-10-24","consentPurpose":"Connect your account and choose business access.",
           "permissions":[{"id":"read","label":"Read workspaces","purpose":"View workspace information.","capabilities":["example.api.read.v1"],"scopeSets":["base"]}]}
          """);
        return node;
    }
}
