namespace Deutchezz.Application.Stays;

/// <summary>
/// Late-check-out / overstay pricing (v1.14). Extra time beyond the booked nights is charged as a
/// percentage of the nightly rate rather than a fixed fee, so it scales with every room's price.
/// The overage is the wall-clock gap between the check-in and check-out times (see
/// <see cref="StayWindow.OverageHours"/>), which is always below 24h because nights absorb whole
/// days. The marginal price never exceeds one full night.
/// </summary>
public static class StayPricing
{
    /// <summary>
    /// Extra charge for a single booking line: <paramref name="ratePerNight"/> ×
    /// <paramref name="quantity"/>, scaled by the policy band the overage falls in. Zero within the
    /// grace window; <c>HalfDayPercent</c> up to the half-day mark; <c>FullNightPercent</c> beyond it.
    /// </summary>
    public static decimal ExtraCharge(decimal ratePerNight, int quantity, double overageHours, StayPolicySettings policy)
    {
        if (overageHours <= policy.GraceHours)
            return 0m;

        var percent = overageHours <= policy.HalfDayHours ? policy.HalfDayPercent : policy.FullNightPercent;
        if (percent <= 0)
            return 0m;

        return Math.Round(ratePerNight * quantity * percent / 100m, 2, MidpointRounding.AwayFromZero);
    }

    /// <summary>Extra charge for a booking line, reading the overage from the resolved stay window.</summary>
    public static decimal ExtraCharge(decimal ratePerNight, int quantity, StayWindow stay) =>
        ExtraCharge(ratePerNight, quantity, stay.OverageHours, stay.Policy);
}
