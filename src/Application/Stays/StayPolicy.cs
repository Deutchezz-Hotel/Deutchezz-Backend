using Deutchezz.Application.Abstractions;
using Deutchezz.Application.Data;
using Deutchezz.Domain.Common;
using Deutchezz.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Deutchezz.Application.Stays;

/// <summary>The effective, admin-editable stay policy (v1.12).</summary>
public sealed record StayPolicySettings(
    TimeOnly DefaultCheckInTime,
    TimeOnly DefaultCheckOutTime,
    int TimeSlotMinutes,
    TimeOnly EarliestCheckInTime,
    TimeOnly LatestCheckInTime,
    TimeOnly EarliestCheckOutTime,
    TimeOnly LatestCheckOutTime,
    int MaxExtraHoursPerStay,
    int TurnaroundMinutes,
    // v1.14 late-check-out pricing bands (see StayPricing).
    int GraceHours = 2,
    int HalfDayHours = 6,
    int HalfDayPercent = 50,
    int FullNightPercent = 100);

/// <summary>A validated stay window: local dates/times plus the derived UTC instants.</summary>
public sealed record StayWindow(
    DateOnly CheckInDate,
    TimeOnly CheckInTime,
    DateOnly CheckOutDate,
    TimeOnly CheckOutTime,
    int Nights,
    DateTime CheckInAt,
    DateTime CheckOutAt,
    StayPolicySettings Policy)
{
    /// <summary>
    /// Hours the stay runs past its booked nights (v1.14). Because <see cref="Nights"/> is the
    /// calendar-day difference, this is simply the check-out minus check-in wall-clock time and is
    /// therefore always below 24h. Negative values (an early check-out time) clamp to zero.
    /// </summary>
    public double OverageHours => Math.Max(0, (CheckOutAt - CheckInAt).TotalHours - Nights * 24.0);
}

public interface IStayPolicyService
{
    /// <summary>The effective stay policy (DB keys overriding appsettings defaults).</summary>
    Task<StayPolicySettings> GetPolicyAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Resolves and validates a requested stay against the stay policy. <paramref name="isStaffWalkIn"/>
    /// relaxes the "in the past" rule for walk-ins (now floored to the slot grid).
    /// </summary>
    Task<Result<StayWindow>> ResolveAsync(
        DateOnly checkInDate,
        string? checkInTime,
        DateOnly checkOutDate,
        string? checkOutTime,
        bool isStaffWalkIn,
        CancellationToken cancellationToken);

    /// <summary>Rebuilds the stay window of an existing booking from its persisted instants.</summary>
    Task<StayWindow> FromBookingAsync(Booking booking, CancellationToken cancellationToken);
}

public class StayPolicyService(
    IAppDbContext db,
    IBusinessClock businessClock,
    ISettingsProvider settings) : IStayPolicyService
{
    private const string DefaultCheckInTimeKey = "DefaultCheckInTime";
    private const string DefaultCheckOutTimeKey = "DefaultCheckOutTime";
    private const string TimeSlotMinutesKey = "TimeSlotMinutes";
    private const string EarliestCheckInTimeKey = "EarliestCheckInTime";
    private const string LatestCheckInTimeKey = "LatestCheckInTime";
    private const string EarliestCheckOutTimeKey = "EarliestCheckOutTime";
    private const string LatestCheckOutTimeKey = "LatestCheckOutTime";
    private const string MaxExtraHoursPerStayKey = "MaxExtraHoursPerStay";
    private const string TurnaroundMinutesKey = "TurnaroundMinutes";
    private const string GraceHoursKey = "GraceHours";
    private const string HalfDayHoursKey = "HalfDayHours";
    private const string HalfDayPercentKey = "HalfDayPercent";
    private const string FullNightPercentKey = "FullNightPercent";

    public async Task<StayPolicySettings> GetPolicyAsync(CancellationToken cancellationToken)
    {
        var stored = await db.Settings
            .Where(s => s.Key == DefaultCheckInTimeKey || s.Key == DefaultCheckOutTimeKey
                || s.Key == TimeSlotMinutesKey || s.Key == EarliestCheckInTimeKey
                || s.Key == LatestCheckInTimeKey || s.Key == EarliestCheckOutTimeKey
                || s.Key == LatestCheckOutTimeKey || s.Key == MaxExtraHoursPerStayKey
                || s.Key == TurnaroundMinutesKey || s.Key == GraceHoursKey
                || s.Key == HalfDayHoursKey || s.Key == HalfDayPercentKey
                || s.Key == FullNightPercentKey
                // Legacy keys (v1.5) remain the fallback for the default times.
                || s.Key == "CheckInTime" || s.Key == "CheckOutTime")
            .ToDictionaryAsync(s => s.Key, s => s.Value, cancellationToken);

        var defaultCheckIn = GetTime(stored, DefaultCheckInTimeKey,
            GetTime(stored, "CheckInTime", settings.DefaultCheckInTime));
        var defaultCheckOut = GetTime(stored, DefaultCheckOutTimeKey,
            GetTime(stored, "CheckOutTime", settings.DefaultCheckOutTime));

        return new StayPolicySettings(
            defaultCheckIn,
            defaultCheckOut,
            GetInt(stored, TimeSlotMinutesKey, settings.TimeSlotMinutes, min: 1, max: 1440),
            GetTime(stored, EarliestCheckInTimeKey, settings.EarliestCheckInTime),
            GetTime(stored, LatestCheckInTimeKey, settings.LatestCheckInTime),
            GetTime(stored, EarliestCheckOutTimeKey, settings.EarliestCheckOutTime),
            GetTime(stored, LatestCheckOutTimeKey, settings.LatestCheckOutTime),
            GetInt(stored, MaxExtraHoursPerStayKey, settings.MaxExtraHoursPerStay, min: 0, max: 720),
            GetInt(stored, TurnaroundMinutesKey, settings.TurnaroundMinutes, min: 0, max: 1440),
            GetInt(stored, GraceHoursKey, settings.GraceHours, min: 0, max: 24),
            GetInt(stored, HalfDayHoursKey, settings.HalfDayHours, min: 0, max: 24),
            GetInt(stored, HalfDayPercentKey, settings.HalfDayPercent, min: 0, max: 100),
            GetInt(stored, FullNightPercentKey, settings.FullNightPercent, min: 0, max: 100));
    }

    public async Task<Result<StayWindow>> ResolveAsync(
        DateOnly checkInDate,
        string? checkInTime,
        DateOnly checkOutDate,
        string? checkOutTime,
        bool isStaffWalkIn,
        CancellationToken cancellationToken)
    {
        var policy = await GetPolicyAsync(cancellationToken);

        if (checkOutDate <= checkInDate)
            return Error.Validation("stay.invalid_range", "Check-out must be after check-in");

        var checkIn = ResolveTime(checkInTime, policy.DefaultCheckInTime);
        if (checkIn is null)
            return Error.Validation("stay.time_not_allowed", "Check-in time must be a valid time such as 14:00");
        var checkOut = ResolveTime(checkOutTime, policy.DefaultCheckOutTime);
        if (checkOut is null)
            return Error.Validation("stay.time_not_allowed", "Check-out time must be a valid time such as 12:00");

        if (!IsOnSlotGrid(checkIn.Value, policy.TimeSlotMinutes) || !IsOnSlotGrid(checkOut.Value, policy.TimeSlotMinutes))
            return Error.Validation("stay.time_not_allowed",
                $"Check-in and check-out times must align to {policy.TimeSlotMinutes}-minute intervals");

        if (checkIn.Value < policy.EarliestCheckInTime || checkIn.Value > policy.LatestCheckInTime)
            return Error.Validation("stay.time_not_allowed",
                $"Check-in time must be between {policy.EarliestCheckInTime:HH\\:mm} and {policy.LatestCheckInTime:HH\\:mm}");
        if (checkOut.Value < policy.EarliestCheckOutTime || checkOut.Value > policy.LatestCheckOutTime)
            return Error.Validation("stay.time_not_allowed",
                $"Check-out time must be between {policy.EarliestCheckOutTime:HH\\:mm} and {policy.LatestCheckOutTime:HH\\:mm}");

        var lagosOffset = businessClock.ConvertFromUtc(businessClock.UtcNow).Offset;
        var checkInAt = ToUtc(checkInDate, checkIn.Value, lagosOffset);
        var checkOutAt = ToUtc(checkOutDate, checkOut.Value, lagosOffset);

        if (checkOutAt <= checkInAt)
            return Error.Validation("stay.invalid_range", "Check-out must be after check-in");

        // v1.14: a stay may run past its booked nights; the overage is charged rather than rejected
        // (see StayPricing). Only the ordering and past-time rules still reject.

        var nowUtc = businessClock.UtcNow.UtcDateTime;
        var earliestAllowedCheckIn = isStaffWalkIn
            ? FloorToSlot(nowUtc, lagosOffset, policy.TimeSlotMinutes)
            : nowUtc;
        if (checkInAt < earliestAllowedCheckIn)
            return Error.Validation("stay.in_the_past", "Check-in time cannot be in the past");

        var nights = checkOutDate.DayNumber - checkInDate.DayNumber;

        return new StayWindow(
            checkInDate, checkIn.Value, checkOutDate, checkOut.Value, nights, checkInAt, checkOutAt, policy);
    }

    public async Task<StayWindow> FromBookingAsync(Booking booking, CancellationToken cancellationToken)
    {
        var policy = await GetPolicyAsync(cancellationToken);
        var lagosOffset = businessClock.ConvertFromUtc(businessClock.UtcNow).Offset;
        return new StayWindow(
            booking.CheckInDate,
            booking.CheckInLocalTime(lagosOffset),
            booking.CheckOutDate,
            booking.CheckOutLocalTime(lagosOffset),
            booking.Nights,
            booking.CheckInAt,
            booking.CheckOutAt,
            policy);
    }

    private static TimeOnly? ResolveTime(string? value, TimeOnly fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
            return fallback;
        return TimeOnly.TryParse(value, out var parsed) ? parsed : null;
    }

    private static bool IsOnSlotGrid(TimeOnly time, int slotMinutes) =>
        (int)time.ToTimeSpan().TotalMinutes % slotMinutes == 0;

    private static DateTime ToUtc(DateOnly date, TimeOnly time, TimeSpan offset) =>
        DateTime.SpecifyKind(new DateTimeOffset(date.ToDateTime(time), offset).UtcDateTime, DateTimeKind.Utc);

    private static DateTime FloorToSlot(DateTime utcNow, TimeSpan lagosOffset, int slotMinutes)
    {
        var local = DateTime.SpecifyKind(utcNow.Add(lagosOffset), DateTimeKind.Unspecified);
        var flooredMinutes = (int)(local.TimeOfDay.TotalMinutes / slotMinutes) * slotMinutes;
        var flooredLocal = local.Date.AddMinutes(flooredMinutes);
        return DateTime.SpecifyKind(new DateTimeOffset(flooredLocal, lagosOffset).UtcDateTime, DateTimeKind.Utc);
    }

    private static TimeOnly GetTime(IReadOnlyDictionary<string, string> stored, string key, TimeOnly fallback) =>
        stored.TryGetValue(key, out var value) && TimeOnly.TryParse(value, out var parsed) ? parsed : fallback;

    private static int GetInt(IReadOnlyDictionary<string, string> stored, string key, int fallback, int min, int max) =>
        stored.TryGetValue(key, out var value) && int.TryParse(value, out var parsed) && parsed >= min && parsed <= max
            ? parsed
            : fallback;
}
