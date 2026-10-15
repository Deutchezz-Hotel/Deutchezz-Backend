using Deutchezz.Application.Bookings;
using Deutchezz.Domain.Enums;

namespace Deutchezz.UnitTests.Bookings;

public class CheckOutReminderPolicyTests
{
    private static readonly DateTime NowUtc = new(2026, 10, 15, 10, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(BookingStatus.CheckedIn)]
    [InlineData(BookingStatus.Pending)]
    [InlineData(BookingStatus.Confirmed)]
    [InlineData(BookingStatus.CheckedOut)]
    [InlineData(BookingStatus.Cancelled)]
    public void Only_a_checked_in_booking_can_be_due(BookingStatus status)
    {
        var due = CheckOutReminderPolicy.IsDue(
            status, NowUtc.AddMinutes(20), null, NowUtc, leadMinutes: 30);
        Assert.Equal(status == BookingStatus.CheckedIn, due);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(29)]
    [InlineData(30)]
    public void A_checked_in_booking_within_the_lead_window_is_due(int minutesAway)
    {
        Assert.True(CheckOutReminderPolicy.IsDue(
            BookingStatus.CheckedIn, NowUtc.AddMinutes(minutesAway), null, NowUtc, leadMinutes: 30));
    }

    [Theory]
    [InlineData(31)]
    [InlineData(60)]
    public void A_checked_in_booking_beyond_the_lead_window_is_not_due(int minutesAway)
    {
        Assert.False(CheckOutReminderPolicy.IsDue(
            BookingStatus.CheckedIn, NowUtc.AddMinutes(minutesAway), null, NowUtc, leadMinutes: 30));
    }

    [Fact]
    public void An_already_overdue_booking_is_still_due()
    {
        Assert.True(CheckOutReminderPolicy.IsDue(
            BookingStatus.CheckedIn, NowUtc.AddMinutes(-120), null, NowUtc, leadMinutes: 30));
    }

    [Fact]
    public void A_booking_already_reminded_is_not_due_again()
    {
        Assert.False(CheckOutReminderPolicy.IsDue(
            BookingStatus.CheckedIn, NowUtc.AddMinutes(20), NowUtc.AddMinutes(-10), NowUtc, leadMinutes: 30));
    }

    [Fact]
    public void A_non_positive_lead_is_treated_as_one_minute()
    {
        Assert.True(CheckOutReminderPolicy.IsDue(
            BookingStatus.CheckedIn, NowUtc.AddSeconds(30), null, NowUtc, leadMinutes: 0));
    }

    [Theory]
    [InlineData(null, "desk@example.com", new[] { "desk@example.com" })]
    [InlineData("", "desk@example.com", new[] { "desk@example.com" })]
    [InlineData("   ", "desk@example.com", new[] { "desk@example.com" })]
    [InlineData(null, null, new string[0])]
    public void Recipients_fall_back_to_the_configured_desk_inbox(string? configured, string? fallback, string[] expected)
    {
        Assert.Equal(expected, CheckOutReminderPolicy.ResolveRecipients(configured, fallback));
    }

    [Fact]
    public void Recipients_are_trimmed_split_and_deduplicated()
    {
        var recipients = CheckOutReminderPolicy.ResolveRecipients(
            " front@hotel.com ; second@hotel.com, FRONT@hotel.com,,third@hotel.com ", "desk@hotel.com");

        Assert.Equal(["front@hotel.com", "second@hotel.com", "third@hotel.com"], recipients);
    }

    [Fact]
    public void An_empty_override_falls_back_to_the_admin_inbox()
    {
        var recipients = CheckOutReminderPolicy.ResolveRecipients("", "admin@hotel.com");
        Assert.Equal(["admin@hotel.com"], recipients);
    }

    [Theory]
    [InlineData(1200, 1200)]
    [InlineData(30.5, 31)]
    [InlineData(0, 0)]
    [InlineData(-90, 0)]
    public void Minutes_until_check_out_ceils_upwards_and_clamps_at_zero(double minutesAway, int expected)
    {
        Assert.Equal(expected, CheckOutReminderPolicy.MinutesUntilCheckOut(NowUtc.AddMinutes(minutesAway), NowUtc));
    }
}
