using Deutchezz.Application.Stays;

namespace Deutchezz.UnitTests.Stays;

public class StayPricingTests
{
    private static StayPolicySettings Policy(
        int grace = 2, int halfDay = 6, int halfPercent = 50, int fullPercent = 100) =>
        new(TimeOnly.Parse("14:00"), TimeOnly.Parse("12:00"), 30,
            TimeOnly.Parse("00:00"), TimeOnly.Parse("23:30"),
            TimeOnly.Parse("00:00"), TimeOnly.Parse("23:30"), 6, 0,
            grace, halfDay, halfPercent, fullPercent);

    [Fact]
    public void Overage_within_the_grace_window_is_free()
    {
        Assert.Equal(0m, StayPricing.ExtraCharge(30000m, 1, 0.0, Policy()));
        Assert.Equal(0m, StayPricing.ExtraCharge(30000m, 1, 2.0, Policy()));
    }

    [Fact]
    public void Half_day_band_charges_half_the_nightly_rate()
    {
        Assert.Equal(15000m, StayPricing.ExtraCharge(30000m, 1, 2.5, Policy()));
        Assert.Equal(15000m, StayPricing.ExtraCharge(30000m, 1, 6.0, Policy()));
    }

    [Fact]
    public void Past_the_half_day_mark_charges_a_full_night()
    {
        Assert.Equal(30000m, StayPricing.ExtraCharge(30000m, 1, 6.1, Policy()));
        Assert.Equal(30000m, StayPricing.ExtraCharge(30000m, 1, 23.0, Policy()));
    }

    [Fact]
    public void The_charge_scales_with_room_rate_and_quantity()
    {
        Assert.Equal(20000m, StayPricing.ExtraCharge(40000m, 1, 4.0, Policy()));
        Assert.Equal(55000m, StayPricing.ExtraCharge(55000m, 1, 9.0, Policy()));
        Assert.Equal(30000m, StayPricing.ExtraCharge(30000m, 2, 3.0, Policy()));
    }

    [Fact]
    public void A_zero_percent_band_charges_nothing()
    {
        Assert.Equal(0m, StayPricing.ExtraCharge(30000m, 1, 4.0, Policy(halfPercent: 0, fullPercent: 0)));
    }

    [Fact]
    public void StayWindow_overage_is_the_clock_time_difference()
    {
        var policy = Policy();
        var checkInAt = new DateTime(2026, 10, 1, 13, 0, 0, DateTimeKind.Utc);   // 14:00 Lagos
        var checkOutAt = new DateTime(2026, 10, 2, 16, 0, 0, DateTimeKind.Utc);  // 17:00 Lagos next day

        var stay = new StayWindow(
            new DateOnly(2026, 10, 1), new TimeOnly(14, 0),
            new DateOnly(2026, 10, 2), new TimeOnly(17, 0),
            1, checkInAt, checkOutAt, policy);

        Assert.Equal(3.0, stay.OverageHours, 3);
        Assert.Equal(15000m, StayPricing.ExtraCharge(30000m, 1, stay));
    }
}
