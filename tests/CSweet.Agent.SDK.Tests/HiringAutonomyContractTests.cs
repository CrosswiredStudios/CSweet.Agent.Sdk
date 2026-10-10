using System.Text.Json;
using Xunit;

namespace CSweet.Agent.SDK.Tests;

public sealed class HiringAutonomyContractTests
{
    [Fact]
    public void DefaultsRequireHumanHireConfirmationAndPreferFirstParty()
    {
        var settings = new HiringPolicySettings();
        Assert.Equal(HiringSelectionMode.RecommendCandidates, settings.Mode);
        Assert.Equal(HiringPublisherPreference.PreferFirstParty, settings.Publishers);
        Assert.Null(settings.MaximumHireCost);
        Assert.Null(settings.AllowedCapabilities);
    }

    [Fact]
    public void BoundedPermissionListsPreserveExplicitEmptyAuthorityAcrossSerialization()
    {
        var settings = new HiringPolicySettings(HiringSelectionMode.Automatic, AutomaticHiringLevel.WithinLimits,
            MaximumHireCost: 0, MaximumTotalCost: 0, Currency: "USD", AllowedCapabilities: [], AllowedNetworkAccess: []);
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var result = JsonSerializer.Deserialize<HiringPolicySettings>(JsonSerializer.Serialize(settings, options), options)!;
        Assert.Empty(result.AllowedCapabilities!);
        Assert.Empty(result.AllowedNetworkAccess!);
        Assert.Equal(AutomaticHiringLevel.WithinLimits, result.AutomaticLevel);
    }
}
