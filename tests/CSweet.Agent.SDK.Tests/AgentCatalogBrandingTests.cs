using System.Text.Json;
using CSweet.Agent.Contracts.Packaging;

namespace CSweet.Agent.SDK.Tests;

public sealed class AgentCatalogBrandingTests
{
    [Fact]
    public void BrandingRoundTripsAndLegacyCatalogsRemainValid()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var catalog = JsonSerializer.Deserialize<AgentCatalogMetadata>("""
            {"imageUrl":"https://assets.example.com/maya.webp","companyLogoUrl":"https://assets.example.com/logo.svg",
             "accentColor":"#1C6252","longDescription":"Delivers product requirements and a roadmap."}
            """, options)!;
        Assert.Equal("#1C6252", catalog.AccentColor);
        var roundTrip = JsonSerializer.Deserialize<AgentCatalogMetadata>(JsonSerializer.Serialize(catalog, options), options)!;
        Assert.Equal(catalog.ImageUrl, roundTrip.ImageUrl);
        Assert.Equal(catalog.CompanyLogoUrl, roundTrip.CompanyLogoUrl);
        Assert.Equal(catalog.LongDescription, roundTrip.LongDescription);
        Assert.Equal(catalog.AccentColor, roundTrip.AccentColor);
        Assert.Empty(AgentCatalogBranding.Validate(catalog.ImageUrl, catalog.CompanyLogoUrl, catalog.AccentColor, catalog.LongDescription));
        Assert.Empty(AgentCatalogBranding.Validate(null, "", "  ", null));
    }

    [Theory]
    [InlineData("http://example.com/image.png")]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:image/svg+xml,anything")]
    [InlineData("file:///C:/secret.png")]
    [InlineData("//example.com/image.png")]
    [InlineData("https://user:pass@example.com/image.png")]
    public void RejectsInvalidArtwork(string url)
    {
        Assert.False(AgentCatalogBranding.IsImageUrl(url));
        Assert.Equal(2, AgentCatalogBranding.Validate(url, url, null, null).Count);
    }

    [Theory]
    [InlineData("red")]
    [InlineData("#fff")]
    [InlineData("#123456; background:url(https://example.com)")]
    [InlineData("#123456\n")]
    public void RejectsNonHexAccent(string color) =>
        Assert.Single(AgentCatalogBranding.Validate(null, null, color, null));

    [Fact]
    public void EnforcesDescriptionAndImageBounds()
    {
        Assert.Empty(AgentCatalogBranding.Validate(null, null, null, new string('a', 2000)));
        Assert.Single(AgentCatalogBranding.Validate(null, null, null, new string('a', 2001)));
        Assert.False(AgentCatalogBranding.IsImageUrl("https://example.com/" + new string('a', 2048)));
    }
}
