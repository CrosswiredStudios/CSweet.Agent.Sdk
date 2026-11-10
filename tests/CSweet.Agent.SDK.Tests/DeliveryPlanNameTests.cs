namespace CSweet.Agent.SDK.Tests;

public sealed class DeliveryPlanNameTests
{
    [Fact]
    public void Short_increment_names_list_every_epic()
    {
        Assert.Equal("Increment: Core loop, Prism bricks",
            HierarchicalProjectDelivery.IncrementPlanName(["Core loop", "Prism bricks"]));
    }

    [Fact]
    public void A_long_single_epic_title_is_shortened_to_the_platform_limit()
    {
        // Observed on 2026-11-10: this 204-character name was rejected on every retry.
        const string title = "[planning-baseline] Producer provisional plan, capacity forecast, staffing proposal, " +
            "and Video Game Director-accepted planning baseline after Creative Director creative-consistency confirmation";

        var name = HierarchicalProjectDelivery.IncrementPlanName([title]);

        Assert.Equal(HierarchicalProjectDelivery.MaxPlanNameLength, name.Length);
        Assert.StartsWith("Increment: [planning-baseline] Producer provisional plan", name);
        Assert.EndsWith("…", name);
        Assert.Equal(name, HierarchicalProjectDelivery.IncrementPlanName([title]));
    }

    [Fact]
    public void Many_epics_name_the_first_and_count_the_rest()
    {
        var titles = Enumerable.Range(1, 12).Select(i => $"Epic number {i} with a reasonably descriptive title").ToArray();

        var name = HierarchicalProjectDelivery.IncrementPlanName(titles);

        Assert.InRange(name.Length, 1, HierarchicalProjectDelivery.MaxPlanNameLength);
        Assert.Equal("Increment: Epic number 1 with a reasonably descriptive title (+11 more)", name);
    }

    [Fact]
    public void Long_first_title_with_others_keeps_the_count_within_the_limit()
    {
        var name = HierarchicalProjectDelivery.IncrementPlanName([new string('a', 300), "Second"]);

        Assert.Equal(HierarchicalProjectDelivery.MaxPlanNameLength, name.Length);
        Assert.EndsWith("… (+1 more)", name);
    }

    [Fact]
    public void A_cut_never_splits_a_surrogate_pair()
    {
        // Place an emoji so the cut point falls between its two UTF-16 code units.
        var budget = HierarchicalProjectDelivery.MaxPlanNameLength - 1 - "Increment: ".Length;
        var title = new string('a', budget - 1) + "\U0001F48E" + new string('b', 20);

        var name = HierarchicalProjectDelivery.IncrementPlanName([title]);

        Assert.True(name.Length <= HierarchicalProjectDelivery.MaxPlanNameLength);
        Assert.False(char.IsHighSurrogate(name[^2]));
        Assert.EndsWith("…", name);
    }

    [Fact]
    public void Blank_titles_still_produce_a_valid_name()
    {
        Assert.Equal("Increment", HierarchicalProjectDelivery.IncrementPlanName(["", "  "]));
    }
}

public sealed class DeliveryPlanRejectionTests
{
    [Theory]
    [InlineData(false, true)]
    [InlineData(null, true)]
    [InlineData(true, false)]
    public void Configure_conflicts_are_rejections_unless_the_platform_marks_them_retryable(bool? retryable, bool rejected)
    {
        var error = new CSweet.Agent.SDK.PlatformCapabilityException(CSweet.WorkManagement.Contracts.WorkDeliveryCapabilities.Configure,
            PlatformCapabilityErrorCode.Conflict, "The delivery plan name is 204 characters; the limit is 160.", retryable: retryable);

        Assert.Equal(rejected, HierarchicalProjectDelivery.IsRequestRejection(error));
    }

    [Fact]
    public void Conflicts_from_other_delivery_capabilities_are_not_rejections()
    {
        var error = new CSweet.Agent.SDK.PlatformCapabilityException(CSweet.WorkManagement.Contracts.WorkDeliveryCapabilities.Control,
            PlatformCapabilityErrorCode.Conflict, "The workflow plan changed.", retryable: false);

        Assert.False(HierarchicalProjectDelivery.IsRequestRejection(error));
    }
}
