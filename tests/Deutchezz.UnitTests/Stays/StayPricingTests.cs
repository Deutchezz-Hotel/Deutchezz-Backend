using Deutchezz.Application.Stays;

namespace Deutchezz.UnitTests.Stays;

public class StayPricingTests
{
    private static readonly IReadOnlyList<SurchargeTier> Standard = StayPricing.DefaultTiersFor("Standard");
    private static readonly IReadOnlyList<SurchargeTier> Deluxe = StayPricing.DefaultTiersFor("Deluxe");
    private static readonly IReadOnlyList<SurchargeTier> Executive = StayPricing.DefaultTiersFor("Executive");

    private const int Grace = 2;

    private static LinePricing Price(decimal rate, int quantity, int nights, double hours, IReadOnlyList<SurchargeTier>? tiers = null) =>
        StayPricing.ComputeLine(rate, quantity, nights, TimeSpan.FromHours(hours), tiers ?? Standard, Grace);

    [Fact]
    public void A_normal_single_night_is_charged_one_nightly_rate()
    {
        // 14:00 to 12:00 next day = 22h: below the booked 24h, so no late-check-out surcharge.
        var priced = Price(30000m, 1, 1, 22.0);
        Assert.Equal(1, priced.Nights);
        Assert.Equal(0, priced.OverageMinutes);
        Assert.Equal(0m, priced.OverageAmount);
        Assert.Equal(30000m, priced.LineTotal);
    }

    [Fact]
    public void Two_booked_nights_charge_two_rates()
    {
        var priced = Price(30000m, 1, 2, 46.0);
        Assert.Equal(2, priced.Nights);
        Assert.Equal(0, priced.OverageAmount);
        Assert.Equal(60000m, priced.LineTotal);
    }

    [Fact]
    public void A_two_hour_remainder_is_within_the_grace_window()
    {
        // 11 PM for one booked night, leaving at 1 AM two dates later: 26h = 1 night + 2h grace.
        var priced = Price(30000m, 1, 1, 26.0);
        Assert.Equal(1, priced.Nights);
        Assert.Equal(120, priced.OverageMinutes);
        Assert.Equal(0m, priced.OverageAmount);
        Assert.Equal(30000m, priced.LineTotal);
    }

    [Fact]
    public void The_stay_is_measured_in_whole_minutes()
    {
        Assert.Equal(0m, StayPricing.ComputeLine(30000m, 1, 1, TimeSpan.FromMinutes(24 * 60 + 120), Standard, Grace).OverageAmount);
        Assert.Equal(5000m, StayPricing.ComputeLine(30000m, 1, 1, TimeSpan.FromMinutes(24 * 60 + 121), Standard, Grace).OverageAmount);
    }

    [Fact]
    public void A_28_hour_stay_bills_one_night_plus_the_first_tier()
    {
        var priced = Price(30000m, 1, 1, 28.0);
        Assert.Equal(1, priced.Nights);
        Assert.Equal(5000m, priced.OverageAmount);
        Assert.Equal("2-5h", priced.TierLabel);
        Assert.Equal(35000m, priced.LineTotal);
    }

    [Fact]
    public void A_33_hour_stay_bills_one_night_plus_the_mid_tier()
    {
        var priced = Price(30000m, 1, 1, 33.0);
        Assert.Equal(1, priced.Nights);
        Assert.Equal(10000m, priced.OverageAmount);
        Assert.Equal(40000m, priced.LineTotal);
    }

    [Fact]
    public void A_54_hour_stay_bills_two_nights_plus_the_mid_tier()
    {
        var priced = Price(30000m, 1, 2, 54.0);
        Assert.Equal(2, priced.Nights);
        Assert.Equal(6 * 60, priced.OverageMinutes);
        Assert.Equal(10000m, priced.OverageAmount);
        Assert.Equal(70000m, priced.LineTotal);
    }

    [Theory]
    [InlineData(120, 0)]     // exactly the grace window
    [InlineData(121, 5000)]  // one minute past the grace window
    [InlineData(300, 5000)]  // top of the first band
    [InlineData(301, 10000)] // start of the mid band
    [InlineData(600, 10000)] // top of the mid band
    [InlineData(601, 15000)] // start of the top band
    [InlineData(900, 15000)] // top of the last band
    [InlineData(901, 30000)] // past the last band -> one full night
    public void Overage_bands_land_on_exact_minute_boundaries(int overageMinutes, decimal overage)
    {
        var duration = TimeSpan.FromMinutes(24 * 60 + overageMinutes);
        var priced = StayPricing.ComputeLine(30000m, 1, 1, duration, Standard, Grace);
        Assert.Equal(overageMinutes, priced.OverageMinutes);
        Assert.Equal(overage, priced.OverageAmount);
    }

    [Fact]
    public void An_overage_within_grace_is_free_even_without_configured_tiers()
    {
        var priced = StayPricing.ComputeLine(30000m, 1, 1, TimeSpan.FromHours(25), [], Grace);
        Assert.Equal(0m, priced.OverageAmount);
    }

    [Fact]
    public void No_configured_tiers_fall_back_to_a_full_night_past_grace()
    {
        var priced = StayPricing.ComputeLine(30000m, 1, 1, TimeSpan.FromHours(30), [], Grace);
        Assert.Equal(30000m, priced.OverageAmount);
        Assert.Equal("full-night", priced.TierLabel);
    }

    [Fact]
    public void The_charge_scales_with_quantity()
    {
        var priced = Price(30000m, 2, 1, 28.0);
        Assert.Equal(5000m * 2, priced.OverageAmount);
        Assert.Equal(70000m, priced.LineTotal);
    }

    [Theory]
    [InlineData("Deluxe", 40000, 28.0, 5000)]
    [InlineData("Deluxe", 40000, 33.0, 15000)]
    [InlineData("Executive", 55000, 33.0, 20000)]
    [InlineData("Executive", 55000, 40.0, 55000)]
    public void Tiers_are_per_room_type(string name, decimal rate, double hours, decimal expected)
    {
        var tiers = StayPricing.DefaultTiersFor(name);
        var priced = Price(rate, 1, 1, hours, tiers);
        Assert.Equal(expected, priced.OverageAmount);
    }

    [Fact]
    public void StayWindow_derives_the_overage_from_its_instants()
    {
        var checkInAt = new DateTime(2026, 10, 1, 23, 0, 0, DateTimeKind.Utc);
        var checkOutAt = new DateTime(2026, 10, 3, 1, 0, 0, DateTimeKind.Utc); // 26h later

        var stay = new StayWindow(
            new DateOnly(2026, 10, 2), new TimeOnly(0, 0),
            new DateOnly(2026, 10, 3), new TimeOnly(1, 0),
            1, checkInAt, checkOutAt, Policy());

        Assert.Equal(TimeSpan.FromHours(26), stay.Duration);
        Assert.Equal(120, stay.OverageMinutes);
    }

    [Fact]
    public void Default_tier_tables_are_valid()
    {
        Assert.Empty(StayPricing.ValidateTiers(Standard, Grace));
        Assert.Empty(StayPricing.ValidateTiers(Deluxe, Grace));
        Assert.Empty(StayPricing.ValidateTiers(Executive, Grace));
    }

    [Fact]
    public void Tier_validation_flags_gaps_start_and_end()
    {
        var gapped = new List<SurchargeTier> { new(2, 4, 5000m), new(5, 15, 10000m) };
        Assert.Contains(StayPricing.ValidateTiers(gapped, Grace), e => e.Contains("contiguous"));

        var wrongStart = new List<SurchargeTier> { new(3, 5, 5000m), new(5, 15, 10000m) };
        Assert.Contains(StayPricing.ValidateTiers(wrongStart, Grace), e => e.Contains("grace window"));

        var wrongEnd = new List<SurchargeTier> { new(2, 5, 5000m), new(5, 12, 10000m) };
        Assert.Contains(StayPricing.ValidateTiers(wrongEnd, Grace), e => e.Contains("end at 15h"));
    }

    [Fact]
    public void Tier_validation_flags_negative_amounts()
    {
        var negative = new List<SurchargeTier> { new(2, 5, -1m), new(5, 15, 10000m) };
        Assert.Contains(StayPricing.ValidateTiers(negative, Grace), e => e.Contains("negative"));
    }

    private static StayPolicySettings Policy() =>
        new(TimeOnly.Parse("14:00"), TimeOnly.Parse("12:00"), 30,
            TimeOnly.Parse("00:00"), TimeOnly.Parse("23:30"),
            TimeOnly.Parse("00:00"), TimeOnly.Parse("23:30"), 0,
            30, Grace);
}
