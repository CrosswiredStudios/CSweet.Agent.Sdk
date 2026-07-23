using System.Reflection;
using CSweet.Agent.SDK;

namespace CSweet.Agent.SDK.Tests;

public sealed class CapabilityCatalogTests
{
    [Fact]
    public void Catalog_ContainsEveryCanonicalCapabilityExactlyOnce()
    {
        var constants = typeof(CapabilityNames)
            .GetNestedTypes(BindingFlags.Public)
            .SelectMany(type => type.GetFields(BindingFlags.Public | BindingFlags.Static))
            .Where(field => field.IsLiteral && !field.IsInitOnly && field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!)
            .ToArray();

        Assert.Equal(constants.Length, constants.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(
            constants.Order(StringComparer.Ordinal),
            CapabilityCatalog.All.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Catalog_IsOrganizedByOwningService()
    {
        Assert.Equal(
            [
                "agent",
                "assistant",
                "communication",
                "management",
                "memory",
                "platform",
                "plugin",
                "product-management",
                "web"
            ],
            CapabilityCatalog.ByService.Keys.Order(StringComparer.Ordinal));
        Assert.All(
            CapabilityCatalog.ByService,
            group => Assert.All(group.Value, capability => Assert.True(CapabilityCatalog.IsKnown(capability))));
    }
}
