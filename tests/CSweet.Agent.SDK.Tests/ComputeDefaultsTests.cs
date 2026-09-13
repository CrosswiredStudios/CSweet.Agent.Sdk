using System.Text.Json;
using CSweet.Agent.SDK.Compute;
namespace CSweet.Agent.SDK.Tests;
public sealed class ComputeDefaultsTests
{
    [Theory]
    [InlineData("Pending")]
    [InlineData("Running")]
    [InlineData("Ready")]
    public async Task Defaults_use_the_existing_read_grant_and_a_single_selector(string state)
    {
        var id = Guid.NewGuid(); var calls = 0;
        var runtime = new AgentTestRuntime().RegisterCapability<JsonElement, ComputeDefaults>(ComputeCapabilities.Read, (input, _) => {
            calls++; Assert.Single(input.EnumerateObject()); Assert.True(input.GetProperty("defaults").GetBoolean());
            return Task.FromResult(new ComputeDefaults(state, state == "Ready" ? id : null, state == "Ready" ? "linux-test" : null, null));
        });
        var result = await runtime.CreateContext().Platform.Compute.GetDefaultsAsync();
        Assert.Equal(state, result.State); Assert.Equal(1, calls);
        Assert.Equal(state == "Ready" ? id : null, result.WorkstreamId);
    }
}