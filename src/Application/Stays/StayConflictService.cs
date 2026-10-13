using Deutchezz.Application.Abstractions;
using Deutchezz.Application.Data;
using Deutchezz.Domain.Entities;
using Deutchezz.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Deutchezz.Application.Stays;

/// <summary>A hard room-level conflict: another stay occupies the room over the requested window.</summary>
public sealed record RoomConflict(
    Guid RoomId,
    string RoomNumber,
    DateTime OccupiedUntil,
    string? OccupantGuestName,
    string? OccupantBookingReference);

/// <summary>The room is free for the requested stay but not free right now (a guest is still in house).</summary>
public sealed record LateAvailability(Guid RoomId, string RoomNumber, DateTime AvailableFrom);

public interface IStayConflictService
{
    /// <summary>
    /// The single overlap engine (v1.12). Returns one conflict per room (the one that occupies it
    /// longest) where another counted booking physically holds the room over
    /// <paramref name="checkInAt"/> .. <paramref name="checkOutAt"/> + turnaround.
    /// </summary>
    Task<IReadOnlyList<RoomConflict>> FindRoomConflictsAsync(
        IReadOnlyCollection<Guid> roomIds,
        Guid excludeBookingId,
        DateTime checkInAt,
        DateTime checkOutAt,
        int turnaroundMinutes,
        CancellationToken cancellationToken);

    /// <summary>
    /// Summed quantity of counted overlapping bookings on a room type, excluding an optional booking.
    /// Raw occupancy is <c>capacity - this</c>.
    /// </summary>
    Task<int> CountOverlappingQuantityAsync(
        Guid roomTypeId,
        Guid? excludeBookingId,
        DateTime checkInAt,
        DateTime checkOutAt,
        int turnaroundMinutes,
        CancellationToken cancellationToken);

    /// <summary>A room that is free for the stay yet still occupied now, with the moment it frees up.</summary>
    Task<LateAvailability?> FindLateAvailabilityAsync(
        Guid roomId, Guid excludeBookingId, DateTime nowUtc, CancellationToken cancellationToken);

    /// <summary>Whether a room can physically be checked into right now.</summary>
    Task<bool> IsRoomPhysicallyFreeNowAsync(Guid roomId, CancellationToken cancellationToken);
}

public class StayConflictService(IAppDbContext db, IBusinessClock businessClock) : IStayConflictService
{
    public async Task<IReadOnlyList<RoomConflict>> FindRoomConflictsAsync(
        IReadOnlyCollection<Guid> roomIds,
        Guid excludeBookingId,
        DateTime checkInAt,
        DateTime checkOutAt,
        int turnaroundMinutes,
        CancellationToken cancellationToken)
    {
        if (roomIds.Count == 0)
            return Array.Empty<RoomConflict>();

        var now = businessClock.UtcNow.UtcDateTime;
        var newEnd = checkOutAt.AddMinutes(turnaroundMinutes);

        var rows = await db.RoomAssignments
            .Where(ra => ra.ReleasedAt == null && roomIds.Contains(ra.RoomId))
            .Join(OverlappingQuery(excludeBookingId, checkInAt, newEnd, turnaroundMinutes, now),
                ra => ra.BookingId, b => b.Id,
                (ra, b) => new
                {
                    ra.RoomId,
                    b.Status,
                    b.CheckOutAt,
                    b.ActualCheckOutAt,
                    b.BookingReference,
                    GuestFirstName = b.Guest != null ? b.Guest.FirstName : null,
                    GuestLastName = b.Guest != null ? b.Guest.LastName : null
                })
            .ToListAsync(cancellationToken);

        if (rows.Count == 0)
            return Array.Empty<RoomConflict>();

        var numbers = await db.Rooms
            .Where(r => roomIds.Contains(r.Id))
            .ToDictionaryAsync(r => r.Id, r => r.RoomNumber, cancellationToken);

        return rows
            .Select(r =>
            {
                var effectiveEnd = r.Status == BookingStatus.CheckedIn
                    && r.ActualCheckOutAt == null && r.CheckOutAt < now
                        ? now
                        : r.CheckOutAt;
                var name = string.Join(' ',
                    new[] { r.GuestFirstName, r.GuestLastName }.Where(s => !string.IsNullOrWhiteSpace(s)));
                return new RoomConflict(
                    r.RoomId,
                    numbers.GetValueOrDefault(r.RoomId, ""),
                    effectiveEnd,
                    string.IsNullOrWhiteSpace(name) ? null : name,
                    r.BookingReference);
            })
            .GroupBy(c => c.RoomId)
            .Select(g => g.OrderByDescending(c => c.OccupiedUntil).First())
            .ToList();
    }

    public async Task<int> CountOverlappingQuantityAsync(
        Guid roomTypeId,
        Guid? excludeBookingId,
        DateTime checkInAt,
        DateTime checkOutAt,
        int turnaroundMinutes,
        CancellationToken cancellationToken)
    {
        var now = businessClock.UtcNow.UtcDateTime;
        var newEnd = checkOutAt.AddMinutes(turnaroundMinutes);

        var booked = await db.BookingRoomTypes
            .Where(brt => brt.RoomTypeId == roomTypeId)
            .Join(OverlappingQuery(excludeBookingId, checkInAt, newEnd, turnaroundMinutes, now),
                brt => brt.BookingId, b => b.Id, (brt, b) => brt.Quantity)
            .SumAsync(q => (int?)q, cancellationToken) ?? 0;

        return booked;
    }

    public async Task<LateAvailability?> FindLateAvailabilityAsync(
        Guid roomId, Guid excludeBookingId, DateTime nowUtc, CancellationToken cancellationToken)
    {
        var room = await db.Rooms.FirstOrDefaultAsync(r => r.Id == roomId, cancellationToken);
        if (room is null)
            return null;

        var inHouseCheckOuts = await db.RoomAssignments
            .Where(ra => ra.RoomId == roomId && ra.ReleasedAt == null)
            .Join(db.Bookings.Where(b => b.Id != excludeBookingId && b.Status == BookingStatus.CheckedIn),
                ra => ra.BookingId, b => b.Id, (ra, b) => b.CheckOutAt)
            .ToListAsync(cancellationToken);

        if (inHouseCheckOuts.Count > 0)
        {
            var latest = inHouseCheckOuts.Max();
            return new LateAvailability(roomId, room.RoomNumber, latest > nowUtc ? latest : nowUtc);
        }

        if (room.Status == RoomStatus.Occupied)
            return new LateAvailability(roomId, room.RoomNumber, nowUtc);

        return null;
    }

    public async Task<bool> IsRoomPhysicallyFreeNowAsync(Guid roomId, CancellationToken cancellationToken)
    {
        var room = await db.Rooms.FirstOrDefaultAsync(r => r.Id == roomId, cancellationToken);
        if (room is null || !room.IsActive
            || room.Status is RoomStatus.Maintenance or RoomStatus.Inactive or RoomStatus.Occupied)
            return false;

        return !await db.RoomAssignments
            .Where(ra => ra.RoomId == roomId && ra.ReleasedAt == null)
            .Join(db.Bookings.Where(b => b.Status == BookingStatus.CheckedIn),
                ra => ra.BookingId, b => b.Id, (ra, b) => b.Id)
            .AnyAsync(cancellationToken);
    }

    /// <summary>
    /// The shared counted-overlap predicate (v1.12): a booking counts when it is
    /// <c>Pending</c>/<c>Confirmed</c>/<c>CheckedIn</c>, starts before the requested end, and its
    /// effective end (max of its <c>CheckOutAt</c> and now for an un-checked-out guest) plus the
    /// turnaround extends past the requested start.
    /// </summary>
    private IQueryable<Booking> OverlappingQuery(
        Guid? excludeBookingId, DateTime checkInAt, DateTime newEnd, int turnaroundMinutes, DateTime now)
    {
        var query = db.Bookings.Where(b =>
            (b.Status == BookingStatus.Pending
                || b.Status == BookingStatus.Confirmed
                || b.Status == BookingStatus.CheckedIn)
            && b.CheckInAt < newEnd
            && (b.CheckOutAt.AddMinutes(turnaroundMinutes) > checkInAt
                || (b.Status == BookingStatus.CheckedIn
                    && b.ActualCheckOutAt == null
                    && now.AddMinutes(turnaroundMinutes) > checkInAt)));

        if (excludeBookingId is not null)
        {
            var excluded = excludeBookingId.Value;
            query = query.Where(b => b.Id != excluded);
        }

        return query;
    }
}
