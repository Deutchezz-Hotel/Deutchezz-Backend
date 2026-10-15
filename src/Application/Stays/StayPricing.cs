namespace Deutchezz.Application.Stays;

/// <summary>One fixed-amount overstay band for a room type (v1.14).</summary>
public sealed record SurchargeTier(int FromHours, int ToHours, decimal Amount);

/// <summary>
/// The computed price of a single booking line (v1.14). A stay keeps the nightly rate for each
/// booked calendar night (<see cref="Nights"/>); any time beyond <c>Nights × 24h</c> is the
/// <see cref="OverageMinutes"/>, which is free inside the grace window, charged a fixed tier amount,
/// or charged one additional full night when it runs past the last tier.
/// </summary>
public sealed record LinePricing(
    int Nights,
    int OverageMinutes,
    decimal OverageAmount,
    string? TierLabel,
    decimal LineTotal);

/// <summary>
/// Duration-based overstay pricing (v1.14). Prices are unchanged for a normal stay: the base is the
/// nightly rate for each booked calendar night. Only the wall-clock time beyond
/// <c>bookedNights × 24h</c> (a late check-out) is charged, using the room type's fixed-amount tier
/// table. Comparisons use whole minutes so the boundary at the grace window and at each tier is exact.
/// </summary>
public static class StayPricing
{
    /// <summary>Overages up to <see cref="MaxTierHours"/> are tiered; beyond that one full night is charged.</summary>
    public const int MaxTierHours = 15;

    public const int MinutesPerDay = 24 * 60;

    /// <summary>
    /// Prices one booking line (v1.14). The base is <paramref name="rate"/> × <paramref name="quantity"/>
    /// × <paramref name="bookedNights"/> (the booked calendar nights), plus a tiered surcharge on the
    /// whole minutes of <paramref name="duration"/> beyond <c>bookedNights × 24h</c>. A normal 22-hour
    /// overnight (14:00 to 12:00) therefore has no overage and charges exactly one nightly rate.
    /// </summary>
    public static LinePricing ComputeLine(
        decimal rate,
        int quantity,
        int bookedNights,
        TimeSpan duration,
        IReadOnlyList<SurchargeTier> tiers,
        int graceHours)
    {
        var nights = Math.Max(0, bookedNights);
        var wholeMinutes = Math.Max(0, (long)Math.Floor(duration.TotalMinutes));
        var overageMinutes = (int)Math.Min(int.MaxValue, Math.Max(0, wholeMinutes - (long)nights * MinutesPerDay));

        var baseTotal = rate * quantity * nights;
        var (overage, label) = ChargeOverage(rate, quantity, overageMinutes, tiers, graceHours);
        return new LinePricing(nights, overageMinutes, overage, label, baseTotal + overage);
    }

    /// <summary>The surcharge for an overage, free inside the grace window and a full night past the last tier.</summary>
    private static (decimal Amount, string? Label) ChargeOverage(
        decimal rate, int quantity, int overageMinutes, IReadOnlyList<SurchargeTier> tiers, int graceHours)
    {
        if (overageMinutes <= graceHours * 60)
            return (0m, null);

        var tier = FindTier(overageMinutes, tiers);
        return tier is not null
            ? (tier.Amount * quantity, $"{tier.FromHours}-{tier.ToHours}h")
            : (rate * quantity, "full-night");
    }

    /// <summary>The band containing <paramref name="overageMinutes"/>: <c>FromHours &lt; overage ≤ ToHours</c>.</summary>
    public static SurchargeTier? FindTier(int overageMinutes, IReadOnlyList<SurchargeTier> tiers) =>
        tiers
            .Where(t => overageMinutes > t.FromHours * 60 && overageMinutes <= t.ToHours * 60)
            .OrderBy(t => t.FromHours)
            .FirstOrDefault();

    /// <summary>Default tier tables keyed by room type name, used when a type has no stored tiers.</summary>
    public static IReadOnlyList<SurchargeTier> DefaultTiersFor(string roomTypeName) =>
        roomTypeName.Trim().ToLowerInvariant() switch
        {
            "deluxe" => [new(2, 5, 5000m), new(5, 10, 15000m), new(10, 15, 20000m)],
            "executive" => [new(2, 5, 5000m), new(5, 10, 20000m), new(10, 15, 30000m)],
            _ => [new(2, 5, 5000m), new(5, 10, 10000m), new(10, 15, 15000m)]
        };

    /// <summary>
    /// Validates a room type's tier table: contiguous, non-overlapping bands starting at the grace
    /// window and ending at <see cref="MaxTierHours"/>, with non-negative amounts.
    /// </summary>
    public static IReadOnlyList<string> ValidateTiers(IReadOnlyList<SurchargeTier> tiers, int graceHours)
    {
        var errors = new List<string>();
        if (tiers.Count == 0)
            return errors;

        var ordered = tiers.OrderBy(t => t.FromHours).ThenBy(t => t.ToHours).ToList();

        if (ordered[0].FromHours != graceHours)
            errors.Add($"The first tier must start at the grace window ({graceHours}h).");

        if (ordered[^1].ToHours != MaxTierHours)
            errors.Add($"The last tier must end at {MaxTierHours}h.");

        for (var i = 0; i < ordered.Count; i++)
        {
            var tier = ordered[i];
            if (tier.ToHours <= tier.FromHours)
                errors.Add($"Tier {tier.FromHours}-{tier.ToHours}h has an invalid range.");
            if (tier.Amount < 0)
                errors.Add($"Tier {tier.FromHours}-{tier.ToHours}h amount cannot be negative.");
            if (i > 0 && ordered[i - 1].ToHours != tier.FromHours)
                errors.Add($"Tier {tier.FromHours}-{tier.ToHours}h must be contiguous with the previous tier.");
        }

        return errors;
    }
}
