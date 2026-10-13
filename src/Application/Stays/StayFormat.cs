namespace Deutchezz.Application.Stays;

/// <summary>
/// Shared formatting for stay moments (v1.12). "HH:mm" for API payloads; one friendly
/// Lagos-based line for every transactional email: "Check-in: Tuesday 13 October 2026, from 2:00 PM".
/// </summary>
public static class StayFormat
{
    /// <summary>Local wall-clock time as "HH:mm" from a UTC instant.</summary>
    public static string LocalTime(DateTime utcInstant, TimeSpan lagosOffset) =>
        utcInstant.Add(lagosOffset).ToString("HH:mm");

    /// <summary>ISO 8601 with the Lagos offset (+01:00), or null when the instant is unset.</summary>
    public static string? Iso(DateTime? utcInstant, TimeSpan lagosOffset) =>
        utcInstant is null
            ? null
            : new DateTimeOffset(DateTime.SpecifyKind(utcInstant.Value, DateTimeKind.Utc))
                .ToOffset(lagosOffset)
                .ToString("yyyy-MM-dd'T'HH:mm:ssK");

    public static string Iso(DateTime utcInstant, TimeSpan lagosOffset) =>
        new DateTimeOffset(DateTime.SpecifyKind(utcInstant, DateTimeKind.Utc))
            .ToOffset(lagosOffset)
            .ToString("yyyy-MM-dd'T'HH:mm:ssK");

    /// <summary>The email "Check-in/Check-out" line, e.g.
    /// "Check-in: Tuesday 13 October 2026, from 2:00 PM" / "Check-out: … by 12:00 PM".</summary>
    public static string ScheduleLine(string label, DateOnly date, TimeOnly time, bool isCheckIn)
    {
        var day = date.ToString("dddd d MMMM yyyy");
        var hour = time.ToString("h:mm tt");
        return $"{label}: {day}, {(isCheckIn ? "from" : "by")} {hour}";
    }
}