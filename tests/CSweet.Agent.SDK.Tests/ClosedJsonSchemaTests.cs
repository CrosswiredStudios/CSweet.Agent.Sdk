using System.Text.Json;
using Xunit;
namespace CSweet.Agent.SDK.Tests;

public sealed class ClosedJsonSchemaTests
{
    private static JsonElement J(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private static readonly JsonElement Search = J("""
        {"type":"object","additionalProperties":false,"properties":{
          "state":{"type":["string","null"],"maxLength":128},
          "minScore":{"type":["number","null"],"minimum":0,"maximum":100},
          "limit":{"type":"integer","minimum":1,"maximum":100}}}
        """);
    private static readonly JsonElement Lookup = J("""
        {"type":"object","additionalProperties":false,"required":["id"],"properties":{"id":{"type":"string","format":"uuid"}}}
        """);
    private static readonly JsonElement Leads = J("""
        {"type":"array","maxItems":2,"items":{"type":"object","additionalProperties":false,
          "required":["id","note"],"properties":{"id":{"type":"string","maxLength":64},"note":{"type":["string","null"],"maxLength":16}}}}
        """);

    private static List<string> Errors(JsonElement schema, bool objectRoot = false)
    {
        var errors = new List<string>();
        ClosedJsonSchema.ValidateDefinition(schema, "$", errors, objectRoot);
        return errors;
    }

    [Fact]
    public void ReviewedShapesAreValidDefinitions()
    {
        Assert.Empty(Errors(Search, objectRoot: true));
        Assert.Empty(Errors(Lookup, objectRoot: true));
        Assert.Empty(Errors(Leads));
    }

    [Theory]
    [InlineData("""{"type":"object","properties":{}}""")]
    [InlineData("""{"type":"object","additionalProperties":{},"properties":{}}""")]
    [InlineData("""{"type":"string"}""")]
    [InlineData("""{"type":"string","maxLength":5000}""")]
    [InlineData("""{"type":"string","format":"email","maxLength":10}""")]
    [InlineData("""{"type":"string","maxLength":10,"pattern":"^a"}""")]
    [InlineData("""{"type":"array","items":{"type":"integer"}}""")]
    [InlineData("""{"type":"array","maxItems":500,"items":{"type":"integer"}}""")]
    [InlineData("""{"type":["string","integer"],"maxLength":3}""")]
    [InlineData("""{"type":"null"}""")]
    [InlineData("""{"oneOf":[{"type":"string","maxLength":1}]}""")]
    [InlineData("""{"$ref":"#/x"}""")]
    [InlineData("""{"type":"integer","minimum":5,"maximum":1}""")]
    [InlineData("""{"type":"integer","maxLength":3}""")]
    [InlineData("""{"type":"object","additionalProperties":false,"required":["missing"],"properties":{}}""")]
    [InlineData("""{"type":"string","enum":[]}""")]
    [InlineData("""{"type":"integer","enum":["a"]}""")]
    public void OpenUnboundedOrUnsupportedDefinitionsAreRejected(string schema) => Assert.NotEmpty(Errors(J(schema)));

    [Fact]
    public void DeepSchemasAreRejected()
    {
        var schema = """{"type":"integer"}""";
        for (var i = 0; i < 10; i++)
            schema = """{"type":"object","additionalProperties":false,"properties":{"a":""" + schema + "}}";
        Assert.NotEmpty(Errors(J(schema)));
    }

    [Fact]
    public void ObjectRootIsRequiredForInputs() =>
        Assert.NotEmpty(Errors(J("""{"type":"array","maxItems":1,"items":{"type":"integer"}}"""), objectRoot: true));

    [Theory]
    [InlineData("""{}""")]
    [InlineData("""{"state":"TX","limit":5}""")]
    [InlineData("""{"state":null,"minScore":72.5,"limit":100}""")]
    public void ConformingInputsPass(string input) => Assert.Null(ClosedJsonSchema.Validate(Search, J(input)));

    [Theory]
    [InlineData("""{"state":"TX","extra":1}""", "$")]
    [InlineData("""{"limit":0}""", "$.limit")]
    [InlineData("""{"limit":1.5}""", "$.limit")]
    [InlineData("""{"minScore":101}""", "$.minScore")]
    [InlineData("""{"state":5}""", "$.state")]
    [InlineData("""{"state":"T\nX"}""", "$.state")]
    [InlineData("""{"limit":null}""", "$.limit")]
    [InlineData("""[]""", "$")]
    public void NonConformingInputsReportOnlyThePath(string input, string path) =>
        Assert.Equal(path, ClosedJsonSchema.Validate(Search, J(input)));

    [Fact]
    public void DuplicatePropertiesAreRejected() =>
        Assert.NotNull(ClosedJsonSchema.Validate(Search, J("""{"limit":1,"limit":2}""")));

    [Fact]
    public void LongStringsAreRejectedWithoutEchoingThem()
    {
        var path = ClosedJsonSchema.Validate(Search, J($$"""{"state":"{{new string('a', 129)}}"}"""));
        Assert.Equal("$.state", path);
    }

    [Theory]
    [InlineData("""{"id":"6f9619ff-8b86-d011-b42d-00cf4fc964ff"}""", true)]
    [InlineData("""{"id":"6F9619FF8B86D011B42D00CF4FC964FF"}""", false)]
    [InlineData("""{"id":"not-a-uuid"}""", false)]
    [InlineData("""{}""", false)]
    public void UuidAndRequiredAreEnforced(string input, bool valid) =>
        Assert.Equal(valid, ClosedJsonSchema.Validate(Lookup, J(input)) is null);

    [Fact]
    public void OutputsEnforceItemsBoundsAndShape()
    {
        Assert.Null(ClosedJsonSchema.Validate(Leads, J("""[{"id":"a","note":null}]"""), rejectControlCharacters: false));
        Assert.Null(ClosedJsonSchema.Validate(Leads, J("""[{"id":"a","note":"line\nbreak"}]"""), rejectControlCharacters: false));
        Assert.Equal("$", ClosedJsonSchema.Validate(Leads, J("""[{"id":"a","note":null},{"id":"b","note":null},{"id":"c","note":null}]"""), false));
        Assert.Equal("$[0]", ClosedJsonSchema.Validate(Leads, J("""[{"id":"a"}]"""), false));
        Assert.Equal("$[0]", ClosedJsonSchema.Validate(Leads, J("""[{"id":"a","note":null,"ssn":"x"}]"""), false));
        Assert.Equal("$[0].note", ClosedJsonSchema.Validate(Leads, J("""[{"id":"a","note":"seventeen chars.."}]"""), false));
    }

    [Fact]
    public void RemoteToolListingMustMatchReviewedNamesAndTypes()
    {
        var remote = J("""
            {"type":"object","properties":{
              "state":{"type":["string","null"],"description":"State","default":null},
              "minScore":{"type":["number","null"]},
              "limit":{"type":"integer","default":50}}}
            """);
        Assert.True(ClosedJsonSchema.IsCompatibleRemoteInput(Search, remote));

        var renamed = J("""{"type":"object","properties":{"region":{"type":["string","null"]},"minScore":{"type":["number","null"]},"limit":{"type":"integer"}}}""");
        Assert.False(ClosedJsonSchema.IsCompatibleRemoteInput(Search, renamed));

        var nullabilityOnly = J("""{"type":"object","properties":{"state":{"type":"string"},"minScore":{"type":"number"},"limit":{"type":"integer"}}}""");
        Assert.True(ClosedJsonSchema.IsCompatibleRemoteInput(Search, nullabilityOnly));

        var retyped = J("""{"type":"object","properties":{"state":{"type":"integer"},"minScore":{"type":["number","null"]},"limit":{"type":"integer"}}}""");
        Assert.False(ClosedJsonSchema.IsCompatibleRemoteInput(Search, retyped));

        var extra = J("""{"type":"object","properties":{"state":{"type":["string","null"]},"minScore":{"type":["number","null"]},"limit":{"type":"integer"},"companyId":{"type":"string"}}}""");
        Assert.False(ClosedJsonSchema.IsCompatibleRemoteInput(Search, extra));

        var requiresMore = J("""{"type":"object","required":["state"],"properties":{"state":{"type":["string","null"]},"minScore":{"type":["number","null"]},"limit":{"type":"integer"}}}""");
        Assert.False(ClosedJsonSchema.IsCompatibleRemoteInput(Search, requiresMore));

        var lookupRemote = J("""{"type":"object","required":["id"],"properties":{"id":{"type":"string","description":"GUID"}}}""");
        Assert.True(ClosedJsonSchema.IsCompatibleRemoteInput(Lookup, lookupRemote));
    }
}
