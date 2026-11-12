using System.Text.Json;
using CSweet.Agent.SDK;

namespace CSweet.Agent.SDK.Tests;

/// <summary>
/// A successful platform call has already committed its effect. QFN Games, 2026-11-11: the gateway
/// reported a saved resource request as failed, and a retry would have asked for the same resource
/// again. The SDK now reads the saved result when it can and escalates without a retry when it cannot.
/// </summary>
public sealed class PlatformCapabilityResultTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task A_saved_result_of_the_expected_shape_is_read()
    {
        var id = Guid.NewGuid();
        var platform = Runtime(JsonSerializer.SerializeToElement(new { id, status = "AwaitingApproval" }, Json));

        var state = await new PlatformResourceClient(platform).RequestAsync(Request());

        Assert.Equal(id, state.Id);
        Assert.Equal("AwaitingApproval", state.Status);
    }

    [Fact]
    public async Task A_saved_result_of_another_shape_escalates_without_a_retry()
    {
        var platform = Runtime(JsonSerializer.SerializeToElement(new[] { new { id = Guid.NewGuid() } }, Json));

        var error = await Assert.ThrowsAsync<PlatformCapabilityException>(() =>
            new PlatformResourceClient(platform).RequestAsync(Request()));

        Assert.Equal(PlatformCapabilityResult.ContractViolationCode, error.FailureCode);
        Assert.False(error.Retryable);
        Assert.True(PlatformCapabilityResult.IsContractViolation(error));
        Assert.Contains("do not retry", error.Message);
        Assert.Contains(nameof(ResourceRequestState), error.Message);
    }

    [Fact]
    public void An_empty_result_stays_a_validation_failure()
    {
        var error = Assert.Throws<PlatformCapabilityException>(() =>
            PlatformCapabilityResult.Read<ResourceRequestState>("resource.request.v1", JsonSerializer.SerializeToElement<object?>(null), Json));

        Assert.Equal(PlatformCapabilityErrorCode.ValidationFailed, error.Code);
        Assert.Null(error.FailureCode);
        Assert.False(PlatformCapabilityResult.IsContractViolation(error));
    }

    [Fact]
    public void A_list_is_described_by_its_element_type()
    {
        var error = Assert.Throws<PlatformCapabilityException>(() =>
            PlatformCapabilityResult.Read<IReadOnlyList<ResourceRequestState>>("resource.read.v1", JsonSerializer.SerializeToElement(new { }), Json));

        Assert.Contains("IReadOnlyList<ResourceRequestState>", error.Message);
    }

    private static PlatformCapabilityClient Runtime(JsonElement saved) =>
        new AgentTestRuntime()
            .RegisterCapability<ResourceRequest, JsonElement>(ResourceCapabilities.Request, (_, _) => Task.FromResult(saved))
            .CreateContext()
            .Platform;

    private static ResourceRequest Request() =>
        new(Guid.NewGuid(), ResourceCapabilities.BuildToolchain, "node-20", "Build the game", "resource:test");
}
