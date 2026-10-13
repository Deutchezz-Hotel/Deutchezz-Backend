using System.Linq.Expressions;
using Deutchezz.Application.Abstractions;
using Deutchezz.Application.Common;
using Deutchezz.Application.Data;
using Deutchezz.Application.Stays;
using Deutchezz.Domain.Common;
using Deutchezz.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Deutchezz.Application.Rooms;

public class RoomsService(
    IAppDbContext db,
    IActivityLogger activityLogger,
    IBusinessClock businessClock,
    IStayPolicyService stays) : IRoomsService
{
    public async Task<Result<PaginatedResult<RoomListItemResponse>>> ListAsync(RoomQuery query, CancellationToken cancellationToken)
    {
        var localNow = businessClock.ConvertFromUtc(businessClock.UtcNow);
        var lagosOffset = localNow.Offset;
        var nowUtc = DateTime.SpecifyKind(businessClock.UtcNow.UtcDateTime, DateTimeKind.Utc);

        var policy = await stays.GetPolicyAsync(cancellationToken);
        var turnaround = policy.TurnaroundMinutes;

        // The board instant: explicit "at", else legacy "date" at the current Lagos time, else now.
        var atUtc = query.At is DateTime at
            ? LocalToUtc(at, lagosOffset)
            : query.Date is DateOnly date
                ? LocalToUtc(date.ToDateTime(TimeOnly.FromTimeSpan(localNow.TimeOfDay)), lagosOffset)
                : nowUtc;

        // The free range: new freeFrom/freeTo, else legacy availableFrom/availableTo with policy times.
        DateTime? freeFromUtc = null;
        DateTime? freeToUtc = null;
        if (query.FreeFrom is not null || query.FreeTo is not null)
        {
            if (query.FreeFrom is null || query.FreeTo is null)
                return Error.Validation("room.invalid_availability_range",
                    "freeFrom and freeTo must both be provided");
            freeFromUtc = LocalToUtc(query.FreeFrom.Value, lagosOffset);
            freeToUtc = LocalToUtc(query.FreeTo.Value, lagosOffset);
        }
        else if (query.AvailableFrom is not null || query.AvailableTo is not null)
        {
            if (query.AvailableFrom is null || query.AvailableTo is null)
                return Error.Validation("room.invalid_availability_range",
                    "availableFrom and availableTo must both be provided and availableFrom must be before availableTo");
            freeFromUtc = LocalToUtc(query.AvailableFrom.Value.ToDateTime(policy.DefaultCheckInTime), lagosOffset);
            freeToUtc = LocalToUtc(query.AvailableTo.Value.ToDateTime(policy.DefaultCheckOutTime), lagosOffset);
        }

        if (freeFromUtc is not null && freeToUtc is not null && freeFromUtc >= freeToUtc)
            return Error.Validation("room.invalid_availability_range", "freeFrom must be before freeTo");

        // Every live assignment against a counted booking, in one round-trip.
        var occupancy = await db.RoomAssignments
            .Where(ra => ra.ReleasedAt == null)
            .Join(db.Bookings.Where(b =>
                    b.Status == BookingStatus.Pending
                    || b.Status == BookingStatus.Confirmed
                    || b.Status == BookingStatus.CheckedIn),
                ra => ra.BookingId, b => b.Id,
                (ra, b) => new OccupancyRow(
                    ra.RoomId, b.Id, b.BookingReference, b.Status, b.CheckInAt, b.CheckOutAt, b.ActualCheckOutAt,
                    b.Guest != null ? b.Guest.FirstName : null,
                    b.Guest != null ? b.Guest.LastName : null))
            .ToListAsync(cancellationToken);

        var occupantAt = BuildOccupantMap(occupancy, atUtc, nowUtc, turnaround);
        var nextArrival = BuildNextArrivalMap(occupancy, atUtc);
        var busyRoomIds = freeFromUtc is not null && freeToUtc is not null
            ? BuildBusyRooms(occupancy, freeFromUtc.Value, freeToUtc.Value, nowUtc, turnaround)
            : null;

        var q = db.Rooms.AsQueryable();

        if (query.RoomTypeId is not null)
            q = q.Where(r => r.RoomTypeId == query.RoomTypeId);

        if (freeFromUtc is not null)
            q = q.Where(r => r.IsActive && r.Status != RoomStatus.Maintenance && r.Status != RoomStatus.Inactive);

        var board = query.Status ?? RoomBoardFilter.All;
        q = board switch
        {
            RoomBoardFilter.Maintenance => q.Where(r => r.Status == RoomStatus.Maintenance),
            RoomBoardFilter.Inactive => q.Where(r => r.Status == RoomStatus.Inactive),
            RoomBoardFilter.Occupied => q.Where(r => r.Status != RoomStatus.Maintenance && r.Status != RoomStatus.Inactive),
            RoomBoardFilter.Available => q.Where(r => r.Status != RoomStatus.Maintenance && r.Status != RoomStatus.Inactive),
            _ => q
        };

        var joined = q.Join(db.RoomTypes, r => r.RoomTypeId, rt => rt.Id,
            (r, rt) => new { Room = r, RoomTypeName = rt.Name });

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim();
            joined = joined.Where(x => x.Room.RoomNumber.Contains(search) || x.RoomTypeName.Contains(search));
        }

        var rows = await joined
            .Select(x => new RoomRow(
                x.Room.Id, x.Room.RoomNumber, x.Room.RoomTypeId, x.RoomTypeName, x.Room.Status, x.Room.Floor))
            .ToListAsync(cancellationToken);

        var items = new List<RoomListItemResponse>();
        foreach (var row in rows)
        {
            var occupied = occupantAt.GetValueOrDefault(row.Id);
            var effective = row.Status is RoomStatus.Maintenance or RoomStatus.Inactive
                ? row.Status.ToString()
                : occupied is not null ? nameof(RoomStatus.Occupied) : nameof(RoomStatus.Available);

            if (board == RoomBoardFilter.Occupied && effective != nameof(RoomStatus.Occupied))
                continue;
            if (board == RoomBoardFilter.Available && effective != nameof(RoomStatus.Available))
                continue;
            if (busyRoomIds is not null && busyRoomIds.Contains(row.Id))
                continue;

            var arrival = nextArrival.GetValueOrDefault(row.Id);
            var isInHouse = occupied is not null && occupied.Status == BookingStatus.CheckedIn;
            var occupiedUntil = occupied is null ? (DateTime?)null : EffectiveEnd(occupied, nowUtc);
            items.Add(new RoomListItemResponse(
                row.Id, row.RoomNumber, row.RoomTypeId, row.RoomTypeName, row.Status.ToString(), row.Floor,
                effective,
                occupiedUntil is null ? null : StayFormat.Iso(occupiedUntil.Value, lagosOffset),
                occupiedUntil is null ? null : StayFormat.Iso(occupiedUntil.Value.AddMinutes(turnaround), lagosOffset),
                isInHouse ? occupied!.GuestName : null,
                isInHouse ? occupied!.BookingId : null,
                isInHouse ? occupied!.BookingReference : null,
                isInHouse && occupied!.ActualCheckOutAt is null && occupied.CheckOutAt < nowUtc,
                arrival is null ? null : StayFormat.Iso(arrival.CheckInAt, lagosOffset),
                arrival?.GuestName));
        }

        var descending = query.IsDescending;
        items = query.Sort?.Trim().ToLowerInvariant() switch
        {
            "status" => DescendingOrder(items, x => x.EffectiveStatus, descending),
            "floor" => DescendingOrder(items, x => x.Floor, descending),
            "roomtypename" => DescendingOrder(items, x => x.RoomTypeName, descending),
            _ => DescendingOrder(items, x => x.RoomNumber, descending)
        };

        var total = items.Count;
        var paged = items
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToList();

        return new PaginatedResult<RoomListItemResponse>(paged, total, query.Page, query.PageSize);
    }

    private static List<RoomListItemResponse> DescendingOrder(
        List<RoomListItemResponse> source, Func<RoomListItemResponse, string?> key, bool descending) =>
        descending
            ? source.OrderByDescending(key).ToList()
            : source.OrderBy(key).ToList();

    private static DateTime LocalToUtc(DateTime local, TimeSpan offset)
    {
        var unspecified = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        return DateTime.SpecifyKind(new DateTimeOffset(unspecified, offset).UtcDateTime, DateTimeKind.Utc);
    }

    private static DateTime EffectiveEnd(OccupancyRow row, DateTime nowUtc) =>
        row.Status == BookingStatus.CheckedIn && row.ActualCheckOutAt is null && row.CheckOutAt < nowUtc
            ? nowUtc
            : row.CheckOutAt;

    private static Dictionary<Guid, OccupancyRow> BuildOccupantMap(
        List<OccupancyRow> rows, DateTime atUtc, DateTime nowUtc, int turnaround)
    {
        var map = new Dictionary<Guid, OccupancyRow>();
        foreach (var row in rows)
        {
            var end = EffectiveEnd(row, nowUtc);
            if (row.CheckInAt > atUtc || end.AddMinutes(turnaround) <= atUtc)
                continue;
            if (!map.TryGetValue(row.RoomId, out var current) || end < EffectiveEnd(current, nowUtc))
                map[row.RoomId] = row;
        }
        return map;
    }

    private static Dictionary<Guid, OccupancyRow> BuildNextArrivalMap(List<OccupancyRow> rows, DateTime atUtc)
    {
        var map = new Dictionary<Guid, OccupancyRow>();
        foreach (var row in rows)
        {
            if (row.Status == BookingStatus.CheckedIn || row.CheckInAt <= atUtc)
                continue;
            if (!map.TryGetValue(row.RoomId, out var current) || row.CheckInAt < current.CheckInAt)
                map[row.RoomId] = row;
        }
        return map;
    }

    private static HashSet<Guid> BuildBusyRooms(
        List<OccupancyRow> rows, DateTime fromUtc, DateTime toUtc, DateTime nowUtc, int turnaround)
    {
        var busy = new HashSet<Guid>();
        foreach (var row in rows)
        {
            var end = EffectiveEnd(row, nowUtc);
            if (row.CheckInAt < toUtc.AddMinutes(turnaround) && end.AddMinutes(turnaround) > fromUtc)
                busy.Add(row.RoomId);
        }
        return busy;
    }

    public async Task<Result<RoomResponse>> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        var room = await db.Rooms
            .Join(db.RoomTypes, r => r.RoomTypeId, rt => rt.Id,
                (r, rt) => new { Room = r, RTypeName = rt.Name })
            .FirstOrDefaultAsync(x => x.Room.Id == id, cancellationToken);

        if (room is null)
            return Error.NotFound("room.not_found", "Room not found");

        return new RoomResponse(room.Room.Id, room.Room.RoomNumber, room.Room.RoomTypeId, room.RTypeName, room.Room.Status.ToString(), room.Room.Floor, room.Room.IsActive, room.Room.Status.ToString());
    }

    public async Task<Result<RoomResponse>> CreateAsync(CreateRoomRequest request, Guid adminUserId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.RoomNumber))
            return Error.Validation("room.number_required", "Room number is required");

        var roomTypeExists = await db.RoomTypes.AnyAsync(rt => rt.Id == request.RoomTypeId && rt.IsActive, cancellationToken);
        if (!roomTypeExists)
            return Error.Validation("room.invalid_roomtype", "Room type does not exist or is inactive");

        var duplicate = await db.Rooms.FirstOrDefaultAsync(r => r.RoomNumber == request.RoomNumber, cancellationToken);
        if (duplicate is not null)
            return Error.Conflict("room.number_in_use", "A room with that number already exists");

        var room = new Domain.Entities.Room
        {
            RoomNumber = request.RoomNumber,
            RoomTypeId = request.RoomTypeId,
            Floor = request.Floor,
            Status = RoomStatus.Available,
            IsActive = true,
            IsDeleted = false
        };
        db.Rooms.Add(room);
        await db.SaveChangesAsync(cancellationToken);

        await activityLogger.LogAsync(adminUserId, "room.created", "Room", room.Id,
            $"Created room {room.RoomNumber}", cancellationToken);

        return await GetByIdAsync(room.Id, cancellationToken);
    }

    public async Task<Result<RoomResponse>> UpdateAsync(Guid id, UpdateRoomRequest request, Guid adminUserId, CancellationToken cancellationToken)
    {
        var room = await db.Rooms.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (room is null)
            return Error.NotFound("room.not_found", "Room not found");

        if (request.RoomNumber is not null)
        {
            if (string.IsNullOrWhiteSpace(request.RoomNumber))
                return Error.Validation("room.number_required", "Room number cannot be empty");
            var duplicate = await db.Rooms.FirstOrDefaultAsync(r => r.RoomNumber == request.RoomNumber && r.Id != id, cancellationToken);
            if (duplicate is not null)
                return Error.Conflict("room.number_in_use", "A room with that number already exists");
            room.RoomNumber = request.RoomNumber;
        }

        if (request.RoomTypeId is not null)
        {
            var roomTypeExists = await db.RoomTypes.AnyAsync(rt => rt.Id == request.RoomTypeId && rt.IsActive, cancellationToken);
            if (!roomTypeExists)
                return Error.Validation("room.invalid_roomtype", "Room type does not exist or is inactive");
            room.RoomTypeId = request.RoomTypeId.Value;
        }

        if (request.Floor is not null)
            room.Floor = request.Floor;

        await db.SaveChangesAsync(cancellationToken);

        await activityLogger.LogAsync(adminUserId, "room.updated", "Room", room.Id, "Updated room", cancellationToken);

        return await GetByIdAsync(id, cancellationToken);
    }

    public async Task<Result> SetStatusAsync(Guid id, string status, Guid staffUserId, CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<RoomStatus>(status, true, out var parsed))
            return Error.Validation("room.invalid_status", "Invalid room status");

        var room = await db.Rooms.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (room is null)
            return Error.NotFound("room.not_found", "Room not found");

        room.Status = parsed;
        await db.SaveChangesAsync(cancellationToken);

        await activityLogger.LogAsync(staffUserId, "room.status_changed", "Room", room.Id,
            $"Status set to {parsed}", cancellationToken);

        return Result.Success();
    }

    public async Task<Result> DeactivateAsync(Guid id, Guid adminUserId, CancellationToken cancellationToken)
    {
        var room = await db.Rooms.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (room is null)
            return Error.NotFound("room.not_found", "Room not found");

        var hasActiveAssignment = await db.RoomAssignments.AnyAsync(ra => ra.RoomId == id && ra.ReleasedAt == null, cancellationToken);
        if (hasActiveAssignment)
            return Error.Conflict("room.in_use", "Cannot deactivate a room that is currently assigned");

        room.IsActive = false;
        room.Status = RoomStatus.Inactive;
        await db.SaveChangesAsync(cancellationToken);

        await activityLogger.LogAsync(adminUserId, "room.deactivated", "Room", room.Id, null, cancellationToken);

        return Result.Success();
    }

    private sealed record OccupancyRow(
        Guid RoomId,
        Guid BookingId,
        string BookingReference,
        BookingStatus Status,
        DateTime CheckInAt,
        DateTime CheckOutAt,
        DateTime? ActualCheckOutAt,
        string? FirstName,
        string? LastName)
    {
        public string? GuestName =>
            string.Join(' ', new[] { FirstName, LastName }.Where(s => !string.IsNullOrWhiteSpace(s))) is { Length: > 0 } name
                ? name
                : null;
    }

    private sealed record RoomRow(
        Guid Id, string RoomNumber, Guid RoomTypeId, string RoomTypeName, RoomStatus Status, string? Floor);
}
