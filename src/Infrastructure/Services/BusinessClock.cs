using Deutchezz.Application.Abstractions;

namespace Deutchezz.Infrastructure.Services;

public class BusinessClock : IBusinessClock
{
    private readonly TimeZoneInfo _timeZone = ResolveTimeZone();

    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;

    public DateOnly Today => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTime.UtcNow, _timeZone).Date);

    public DateTimeOffset ConvertFromUtc(DateTimeOffset utc) => TimeZoneInfo.ConvertTime(utc, _timeZone);

    public DateOnly ToLocalDate(DateTimeOffset utc) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(utc, _timeZone).Date);

    /// <summary>
    /// Resolves the Africa/Lagos time zone id on both Windows and Linux (.NET uses the IANA id
    /// with ICU on Windows; the Windows registry id is the fallback).
    /// </summary>
    private static TimeZoneInfo ResolveTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Africa/Lagos");
        }
        catch (TimeZoneNotFoundException)
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById("W. Africa Standard Time");
            }
            catch (TimeZoneNotFoundException)
            {
                return TimeZoneInfo.Utc;
            }
        }
    }
}