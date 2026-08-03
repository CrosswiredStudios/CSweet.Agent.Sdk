using System.Reflection;
using CSweet.Agent.SDK;
using CSweet.WorkManagement.Contracts;

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
        Assert.All(CapabilityCatalog.All, capability => Assert.Contains(capability, constants));
        Assert.Contains(WorkManagementCapabilityNames.ItemMove, CapabilityCatalog.All);
        Assert.DoesNotContain(WorkManagementCapabilityNames.AutomationManage, CapabilityCatalog.All);
    }

    [Fact]
    public void Catalog_IsOrganizedByOwningService()
    {
        Assert.Equal(
            [
                "agent",
                "agent-catalog",
                "assistant",
                "communication",
                "git-merge",
                "git-workspace",
                "management",
                "memory",
                "platform",
                "plugin",
                "product-management",
                "source-control",
                "web",
                "work-management"
            ],
            CapabilityCatalog.ByService.Keys.Order(StringComparer.Ordinal));
        Assert.All(
            CapabilityCatalog.ByService,
            group => Assert.All(group.Value, capability => Assert.True(CapabilityCatalog.IsKnown(capability))));
    }

    [Fact]
    public void AgentCatalogSearch_IsCanonical()
    {
        Assert.Equal("platform.agent-catalog.search.v1", AgentCatalogCapabilities.Search);
        Assert.True(CapabilityCatalog.IsKnown(AgentCatalogCapabilities.Search));
        Assert.Contains(AgentCatalogCapabilities.Search, PlatformCapabilities.All);
    }

    [Fact]
    public void WorkManagementCatalog_ExactlyMatchesSharedContract()
    {
        Assert.Equal(
            WorkManagementCapabilityNames.All.Order(StringComparer.Ordinal),
            CapabilityCatalog.ByService["work-management"].Order(StringComparer.Ordinal));
    }
}
