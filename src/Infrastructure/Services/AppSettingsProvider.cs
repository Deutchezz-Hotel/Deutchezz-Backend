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

    private static TimeOnly Parse(string value, string fallback) =>
        TimeOnly.TryParse(value, out var time) ? time : TimeOnly.Parse(fallback);
}