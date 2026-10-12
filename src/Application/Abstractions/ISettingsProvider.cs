namespace Deutchezz.Application.Abstractions;

/// <summary>
/// Business settings read from configuration today; a database-backed implementation
/// (admin settings) arrives later without changing callers.
/// </summary>
public interface ISettingsProvider
{
    int MaxRoomsPerBooking { get; }
    TimeOnly CheckInTime { get; }
    TimeOnly CheckOutTime { get; }
    int CancellationWindowHours { get; }
    bool RequireFullPaymentAtCheckIn { get; }
    bool RequirePaymentBeforeCheckOut { get; }
    bool EarlyCheckOutRequiresAdmin { get; }
}