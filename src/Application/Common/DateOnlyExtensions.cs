namespace Deutchezz.Application.Common;

public static class DateOnlyExtensions
{
    /// <summary>
    /// Midnight UTC for the date. Npgsql maps DateTime to "timestamp with time zone" and rejects
    /// Kind=Unspecified values, so range filters over timestamp columns must build bounds this way.
    /// </summary>
    public static DateTime UtcStartOfDay(this DateOnly date) =>
        DateTime.SpecifyKind(date.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
}
