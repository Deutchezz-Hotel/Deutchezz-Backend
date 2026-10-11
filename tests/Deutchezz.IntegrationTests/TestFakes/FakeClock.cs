using Deutchezz.Application.Abstractions;

namespace Deutchezz.IntegrationTests.TestFakes;

public class FakeClock(DateTimeOffset utcNow, TimeZoneInfo timeZone) : IBusinessClock, IClock
{
    public FakeClock(DateOnly today) : this(new DateTimeOffset(today.Year, today.Month, today.Day, 0, 0, 0, TimeSpan.Zero), Resolve("Africa/Lagos"))
    {
    }

    public DateTimeOffset UtcNow { get; private set; } = utcNow;

    public DateOnly Today => ToLocalDate(UtcNow);

    DateTime IClock.UtcNow => UtcNow.UtcDateTime;

    public void Advance(TimeSpan span) => UtcNow += span;

    public DateTimeOffset ConvertFromUtc(DateTimeOffset utc) => TimeZoneInfo.ConvertTime(utc, timeZone);

    public DateOnly ToLocalDate(DateTimeOffset utc) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(utc, timeZone).Date);

    private static TimeZoneInfo Resolve(string id)
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById(id); } catch { return TimeZoneInfo.Utc; }
    }
}
