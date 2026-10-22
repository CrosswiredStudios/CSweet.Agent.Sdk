using System.Security.Cryptography;
using System.Text.Json;

namespace CSweet.Agent.SDK.Tests;

public sealed class AgentOperatingStateKeyTests
{
    [Fact]
    public void StructuredIdentityIsBoundedStableAndUnambiguous()
    {
        var source = new string('a', 64);
        var provider = Guid.Parse("11111111-2222-3333-4444-555555555555");
        var key = AgentOperatingStateKey.Create("game-project-identity-attempt:v1", new { source, provider, modelName = new string('x', 10000) });
        Assert.True(AgentOperatingStateKey.IsValid(key));
        Assert.True(key.Length <= AgentOperatingStateValidation.MaximumFingerprintLength);
        Assert.Equal(key, AgentOperatingStateKey.Create("game-project-identity-attempt:v1", new { source, provider, modelName = new string('x', 10000) }));
        Assert.NotEqual(key, AgentOperatingStateKey.Create("game-project-identity-attempt:v1", new { source, provider, modelName = "corrected-model" }));
        Assert.NotEqual(AgentOperatingStateKey.Create("test:v1", new[] { "a:b", "c" }), AgentOperatingStateKey.Create("test:v1", new[] { "a", "b:c" }));
        Assert.NotEqual(AgentOperatingStateKey.Create("test:v1", new[] { "a", "b" }), AgentOperatingStateKey.Create("other:v1", new[] { "a", "b" }));
    }

    [Fact]
    public void BuilderRetainsFullDigestAndExistingSingleDigestIdentities()
    {
        var identity = new { title = "RESONANCE", revision = 3 };
        var digest = Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(identity)));
        Assert.Equal("project:v1:" + digest, AgentOperatingStateKey.Create("project:v1", identity));
        var key = AgentOperatingStateKey.Create(new string('a', AgentOperatingStateKey.MaximumScopeLength), identity);
        Assert.Equal(AgentOperatingStateKey.MaximumLength, key.Length);
        Assert.Throws<ArgumentException>(() => AgentOperatingStateKey.Create(new string('a', AgentOperatingStateKey.MaximumScopeLength + 1), identity));
        Assert.Throws<ArgumentNullException>(() => AgentOperatingStateKey.Create<object>("test", null!));
    }

    [Theory]
    [InlineData("")]
    [InlineData("has space")]
    [InlineData("unicode-é")]
    [InlineData("line\n")]
    [InlineData("path?query")]
    public void InvalidKeyCharactersAreRejected(string key)
    {
        Assert.False(AgentOperatingStateKey.IsValid(key));
        Assert.Throws<ArgumentException>(() => AgentOperatingStateKey.Validate(key));
        Assert.Throws<ArgumentException>(() => AgentOperatingStateKey.Create(key, new { id = 1 }));
    }

    [Theory]
    [InlineData(160, true)]
    [InlineData(161, false)]
    [InlineData(162, false)]
    public async Task ClientAndTestRuntimeRejectInvalidKeysBeforeFakeHandler(int length, bool valid)
    {
        var calls = 0;
        var runtime = new AgentTestRuntime().RegisterCapability<AgentOperatingStateReadRequest, AgentOperatingStateReadResponse>(
            PlatformCapabilities.AgentOperatingStateRead, (_, _) => {
                calls++;
                return Task.FromResult(new AgentOperatingStateReadResponse(null));
            });
        var client = runtime.CreateContext().Platform;
        var key = new string('a', length);
        if (valid) await client.ReadOperatingStateAsync(new AgentOperatingStateReadRequest(key));
        else
        {
            var failure = await Assert.ThrowsAsync<PlatformCapabilityException>(() => client.ReadOperatingStateAsync(new AgentOperatingStateReadRequest(key)));
            Assert.Equal(PlatformCapabilityErrorCode.ValidationFailed, failure.Code);
        }
        Assert.Equal(valid ? 1 : 0, calls);
        // Raw tool invocation bypasses the typed client; the test runtime must still validate it.
        if (!valid)
            await Assert.ThrowsAsync<PlatformCapabilityException>(async () => await client.Tools.InvokeAsync(
                PlatformCapabilities.AgentOperatingStateRead, JsonSerializer.SerializeToElement(new { stateKey = key })));
        Assert.Equal(valid ? 1 : 0, calls);
    }

    [Fact]
    public async Task TestRuntimeRejectsOversizedFingerprintEvenForValidKey()
    {
        var called = false;
        var runtime = new AgentTestRuntime().RegisterCapability<AgentOperatingStateWriteRequest, JsonElement>(
            PlatformCapabilities.AgentOperatingStateWrite, (_, _) => { called = true; return Task.FromResult(JsonSerializer.SerializeToElement(new { })); });
        var request = new AgentOperatingStateWriteRequest("valid-key", "test.v1", 1, "Active", new Dictionary<string, string>(), [],
            new string('a', 129), [], Guid.NewGuid(), JsonSerializer.SerializeToElement(new { }), null, "once");
        await Assert.ThrowsAsync<PlatformCapabilityException>(() => runtime.CreateContext().Platform.WriteOperatingStateAsync(request));
        Assert.False(called);
    }

    [Theory]
    [InlineData(128000, true)]
    [InlineData(128001, false)]
    public void SharedValidationUsesSerializedObjectPayloadBoundary(int length, bool accepted)
    {
        var payload = JsonSerializer.SerializeToElement(new { text = new string('a', length - 11) });
        Assert.Equal(length, payload.GetRawText().Length);
        var request = new AgentOperatingStateWriteRequest("checkpoint", "test.v1", 1, "Active", new Dictionary<string, string>(), [],
            "digest", [], Guid.NewGuid(), payload, null, "once");
        Assert.Equal(accepted, Record.Exception(() => AgentOperatingStateValidation.Validate(request)) is null);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"stateKey\":\"valid\",\"unexpected\":true}")]
    [InlineData("{\"stateKey\":null}")]
    public async Task RawTestToolsRejectInvalidRequestShapes(string json)
    {
        var called = false;
        var runtime = new AgentTestRuntime().RegisterCapability<JsonElement, JsonElement>(
            PlatformCapabilities.AgentOperatingStateRead, (_, _) => { called = true; return Task.FromResult(JsonSerializer.SerializeToElement(new { })); });
        await Assert.ThrowsAsync<PlatformCapabilityException>(() => runtime.CreateContext().Platform.Tools.InvokeAsync(
            PlatformCapabilities.AgentOperatingStateRead, JsonSerializer.Deserialize<JsonElement>(json)));
        Assert.False(called);
    }
}
