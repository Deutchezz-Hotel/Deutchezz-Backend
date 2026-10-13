using Deutchezz.Domain.Common;
using Deutchezz.Domain.Enums;

namespace Deutchezz.Domain.Entities;

public class Booking : AuditableEntity
{
    public string BookingReference { get; set; } = string.Empty;
    public Guid GuestId { get; set; }
    public Guest? Guest { get; set; }

    /// <summary>UTC instant the stay begins (v1.12). Source of truth for occupancy.</summary>
    public DateTime CheckInAt { get; set; }

    /// <summary>UTC instant the stay ends (v1.12). Source of truth for occupancy.</summary>
    public DateTime CheckOutAt { get; set; }

    /// <summary>Local (Africa/Lagos) calendar date of check-in. Derived; written only by <see cref="SetStay"/>.</summary>
    public DateOnly CheckInDate { get; set; }

    /// <summary>Local (Africa/Lagos) calendar date of check-out. Derived; written only by <see cref="SetStay"/>.</summary>
    public DateOnly CheckOutDate { get; set; }

    /// <summary>UTC instant the guest was actually checked in (v1.12).</summary>
    public DateTime? ActualCheckInAt { get; set; }

    public int Nights { get; set; }
    public int Adults { get; set; }
    public int Children { get; set; }
    public BookingStatus Status { get; set; } = BookingStatus.Pending;
    public PaymentStatus PaymentStatus { get; set; } = PaymentStatus.Pending;
    public BookingSource Source { get; set; } = BookingSource.Online;
    public string? SpecialRequests { get; set; }
    public decimal TotalAmount { get; set; }
    public DateTime? CancelledAt { get; set; }
    public string? CancellationReason { get; set; }
    public DateTime? ActualCheckOutAt { get; set; }
    public string? EarlyCheckOutReason { get; set; }
    public string? EarlyCheckOutNotes { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public string? IdempotencyKey { get; set; }
    public string? IdempotencyRequestHash { get; set; }
    public byte[]? ConcurrencyToken { get; set; }

    /// <summary>
    /// The single writer of a booking's stay (v1.12). Records the local (Africa/Lagos) check-in
    /// and check-out dates/times and derives the UTC <see cref="CheckInAt"/>/<see cref="CheckOutAt"/>
    /// instants and <see cref="Nights"/> that drive occupancy. <paramref name="lagosOffset"/> is the
    /// current Africa/Lagos offset from UTC (normally +01:00).
    /// </summary>
    public void SetStay(
        DateOnly checkInDate, TimeOnly checkInTime,
        DateOnly checkOutDate, TimeOnly checkOutTime,
        TimeSpan lagosOffset)
    {
        CheckInDate = checkInDate;
        CheckOutDate = checkOutDate;
        Nights = checkOutDate.DayNumber - checkInDate.DayNumber;
        CheckInAt = ToUtc(checkInDate, checkInTime, lagosOffset);
        CheckOutAt = ToUtc(checkOutDate, checkOutTime, lagosOffset);
    }

    /// <summary>The local wall-clock check-in time in Africa/Lagos, derived from <see cref="CheckInAt"/>.</summary>
    public TimeOnly CheckInLocalTime(TimeSpan lagosOffset) =>
        TimeOnly.FromDateTime(CheckInAt.Add(lagosOffset));

    /// <summary>The local wall-clock check-out time in Africa/Lagos, derived from <see cref="CheckOutAt"/>.</summary>
    public TimeOnly CheckOutLocalTime(TimeSpan lagosOffset) =>
        TimeOnly.FromDateTime(CheckOutAt.Add(lagosOffset));

    private static DateTime ToUtc(DateOnly date, TimeOnly time, TimeSpan lagosOffset)
    {
        var offset = new DateTimeOffset(date.ToDateTime(time), lagosOffset);
        return DateTime.SpecifyKind(offset.UtcDateTime, DateTimeKind.Utc);
    }
}