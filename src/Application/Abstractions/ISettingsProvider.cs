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
    int DuplicateWarningMinutes { get; }

    // v1.12 stay policy.
    TimeOnly DefaultCheckInTime { get; }
    TimeOnly DefaultCheckOutTime { get; }
    int TimeSlotMinutes { get; }
    TimeOnly EarliestCheckInTime { get; }
    TimeOnly LatestCheckInTime { get; }
    TimeOnly EarliestCheckOutTime { get; }
    TimeOnly LatestCheckOutTime { get; }
    int MaxExtraHoursPerStay { get; }
    int TurnaroundMinutes { get; }

    // v1.14 late-check-out pricing bands (percentages of the nightly rate).
    int GraceHours { get; }
    int HalfDayHours { get; }
    int HalfDayPercent { get; }
    int FullNightPercent { get; }
}