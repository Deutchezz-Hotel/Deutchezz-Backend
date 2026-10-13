using Deutchezz.Application.Abstractions;
using Deutchezz.Application.Common.Options;
using Microsoft.Extensions.Options;

namespace Deutchezz.Infrastructure.Services;

public class AppSettingsProvider(
    IOptions<SettingsOptions> settings,
    IOptions<CancellationOptions> cancellation,
    IOptions<BookingOptions> booking) : ISettingsProvider
{
    private readonly SettingsOptions _settings = settings.Value;

    public int MaxRoomsPerBooking => _settings.MaxRoomsPerBooking;

    public TimeOnly CheckInTime => Parse(_settings.CheckInTime, "14:00");

    public TimeOnly CheckOutTime => Parse(_settings.CheckOutTime, "12:00");

    public int CancellationWindowHours => cancellation.Value.WindowHours;

    public bool RequireFullPaymentAtCheckIn => _settings.RequireFullPaymentAtCheckIn;

    public bool RequirePaymentBeforeCheckOut => _settings.RequirePaymentBeforeCheckOut;

    public bool EarlyCheckOutRequiresAdmin => _settings.EarlyCheckOutRequiresAdmin;

    public int DuplicateWarningMinutes => booking.Value.DuplicateWarningMinutes;

    public TimeOnly DefaultCheckInTime => Parse(_settings.DefaultCheckInTime, "14:00");

    public TimeOnly DefaultCheckOutTime => Parse(_settings.DefaultCheckOutTime, "12:00");

    public int TimeSlotMinutes => _settings.TimeSlotMinutes > 0 ? _settings.TimeSlotMinutes : 30;

    public TimeOnly EarliestCheckInTime => Parse(_settings.EarliestCheckInTime, "00:00");

    public TimeOnly LatestCheckInTime => Parse(_settings.LatestCheckInTime, "23:30");

    public TimeOnly EarliestCheckOutTime => Parse(_settings.EarliestCheckOutTime, "00:00");

    public TimeOnly LatestCheckOutTime => Parse(_settings.LatestCheckOutTime, "23:30");

    public int MaxExtraHoursPerStay => _settings.MaxExtraHoursPerStay >= 0 ? _settings.MaxExtraHoursPerStay : 6;

    public int TurnaroundMinutes => _settings.TurnaroundMinutes >= 0 ? _settings.TurnaroundMinutes : 0;

    private static TimeOnly Parse(string value, string fallback) =>
        TimeOnly.TryParse(value, out var time) ? time : TimeOnly.Parse(fallback);
}