using Deutchezz.Application.Stays;

namespace Deutchezz.UnitTests.Stays;

public class StayFormatTests
{
    private static readonly TimeSpan Lagos = TimeSpan.FromHours(1);

    [Fact]
    public void ScheduleLine_renders_a_check_in_with_from()
    {
        var line = StayFormat.ScheduleLine("Check-in", new DateOnly(2026, 10, 13), new TimeOnly(14, 0), isCheckIn: true);
        Assert.Equal("Check-in: Tuesday 13 October 2026, from 2:00 PM", line);
    }

    [Fact]
    public void ScheduleLine_renders_a_check_out_with_by()
    {
        var line = StayFormat.ScheduleLine("Check-out", new DateOnly(2026, 10, 13), new TimeOnly(12, 0), isCheckIn: false);
        Assert.Equal("Check-out: Tuesday 13 October 2026, by 12:00 PM", line);
    }

    [Fact]
    public void Iso_renders_the_lagos_offset()
    {
        var utc = new DateTime(2026, 10, 13, 13, 0, 0, DateTimeKind.Utc);
        Assert.Equal("2026-10-13T14:00:00+01:00", StayFormat.Iso(utc, Lagos));
    }

    [Fact]
    public void Iso_is_null_for_an_unset_instant()
    {
        Assert.Null(StayFormat.Iso(null, Lagos));
    }

    [Fact]
    public void LocalTime_formats_from_a_utc_instant()
    {
        var utc = new DateTime(2026, 10, 13, 23, 30, 0, DateTimeKind.Utc);
        Assert.Equal("00:30", StayFormat.LocalTime(utc, Lagos));
    }
}
