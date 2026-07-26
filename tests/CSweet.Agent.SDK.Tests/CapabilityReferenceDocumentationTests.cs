using CSweet.Agent.SDK;

namespace CSweet.Agent.SDK.Tests;

public sealed class CapabilityReferenceDocumentationTests
{
    [Fact]
    public void GrantsReference_ContainsEveryRegisteredCapability()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "GRANTS.md")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        var reference = File.ReadAllText(Path.Combine(directory!.FullName, "GRANTS.md"));

        var missing = CapabilityCatalog.All
            .Where(capability => !reference.Contains($"`{capability}`", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.True(missing.Length == 0,
            "GRANTS.md is stale. Missing capabilities:" + Environment.NewLine +
            string.Join(Environment.NewLine, missing));
    }
}
