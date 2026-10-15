using Deutchezz.Domain.Entities;
using Deutchezz.Domain.Enums;

namespace Deutchezz.Application.Bookings;

/// <summary>
/// Pure, testable rules for the front-desk check-out reminder (v1.16): when a booking is due for a
/// reminder and which inboxes receive it.
/// </summary>
public static class CheckOutReminderPolicy
{
    /// <summary>
    /// True when a booking should receive its one front-desk reminder: it is currently
    /// <see cref="BookingStatus.CheckedIn"/>, no reminder has been sent yet, and its scheduled
    /// check-out falls on or before <paramref name="nowUtc"/> plus the lead window. A booking whose
    /// check-out is already past (an overstaying guest) is still due, so reception can act.
    /// </summary>
    public static bool IsDue(
        BookingStatus status,
        DateTime checkOutAtUtc,
        DateTime? reminderSentAtUtc,
        DateTime nowUtc,
        int leadMinutes)
    {
        if (status != BookingStatus.CheckedIn || reminderSentAtUtc is not null)
            return false;

        var horizon = nowUtc.AddMinutes(Math.Max(1, leadMinutes));
        return checkOutAtUtc <= horizon;
    }

    /// <summary>
    /// Splits a comma/semicolon-separated recipient list, trims blanks and de-duplicates
    /// case-insensitively. Falls back to <paramref name="fallback"/> (the configured desk inbox)
    /// when the override is empty.
    /// </summary>
    public static IReadOnlyList<string> ResolveRecipients(string? configured, string? fallback)
    {
        var source = string.IsNullOrWhiteSpace(configured) ? fallback : configured;
        if (string.IsNullOrWhiteSpace(source))
            return [];

        return source
            .Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>The whole minutes until check-out, clamped at zero for an already-overdue guest.</summary>
    public static int MinutesUntilCheckOut(DateTime checkOutAtUtc, DateTime nowUtc) =>
        (int)Math.Max(0, Math.Ceiling((checkOutAtUtc - nowUtc).TotalMinutes));
}
