namespace CSweet.Agent.SDK.Tests;

public sealed class InfrastructureContractsTests
{
    [Fact]
    public void StateExport_IsDeterministicAndYaml12Compatible()
    {
        var first = InfrastructureStateSerializer.Export(new Dictionary<string, object>
        {
            ["z"] = 2,
            ["a"] = new Dictionary<string, object> { ["later"] = true, ["first"] = "value" }
        });
        var second = InfrastructureStateSerializer.Export(new Dictionary<string, object>
        {
            ["a"] = new Dictionary<string, object> { ["first"] = "value", ["later"] = true },
            ["z"] = 2
        });

        Assert.Equal(first, second);
        Assert.StartsWith("{\"a\":", first.Json, StringComparison.Ordinal);
        Assert.Equal($"---\n{first.Json}\n", first.Yaml);
        Assert.Equal(64, first.ContentHash.Length);
    }
}
