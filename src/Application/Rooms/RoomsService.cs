using System.Linq.Expressions;
using Deutchezz.Application.Abstractions;
using Deutchezz.Application.Common;
using Deutchezz.Application.Data;
using Deutchezz.Domain.Common;
using Deutchezz.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Deutchezz.Application.Rooms;

public class RoomsService(IAppDbContext db, IActivityLogger activityLogger) : IRoomsService
{
    public async Task<Result<PaginatedResult<RoomListItemResponse>>> ListAsync(RoomQuery query, CancellationToken cancellationToken)
    {
        var q = db.Rooms.AsQueryable();

        if (query.RoomTypeId is not null)
            q = q.Where(r => r.RoomTypeId == query.RoomTypeId);

        // Stay-availability range: both ends required, half-open [availableFrom, availableTo).
        // Only active, usable rooms with no live assignment held by a Confirmed/CheckedIn
        // booking whose stay overlaps the range are returned.
        if (query.AvailableFrom is not null || query.AvailableTo is not null)
        {
            if (query.AvailableFrom is null || query.AvailableTo is null || query.AvailableFrom >= query.AvailableTo)
                return Error.Validation("room.invalid_availability_range",
                    "availableFrom and availableTo must both be provided and availableFrom must be before availableTo");

            var from = query.AvailableFrom.Value;
            var to = query.AvailableTo.Value;

            var busyRoomIds = await db.RoomAssignments
                .Where(ra => ra.ReleasedAt == null)
                .Join(db.Bookings.Where(b => (b.Status == BookingStatus.Confirmed || b.Status == BookingStatus.CheckedIn)
                        && b.CheckInDate < to && b.CheckOutDate > from),
                    ra => ra.BookingId, b => b.Id, (ra, b) => ra.RoomId)
                .Distinct()
                .ToListAsync(cancellationToken);

            q = q.Where(r => r.IsActive
                && r.Status != RoomStatus.Maintenance
                && r.Status != RoomStatus.Inactive
                && !busyRoomIds.Contains(r.Id));
        }

        // Date-based board: when a date is supplied, "Occupied" means a room assigned to a
        // Confirmed/CheckedIn booking that covers that date, independent of the stored physical
        // status. Without a date we fall back to the physical status (unchanged behaviour).
        List<Guid>? occupiedRoomIds = null;
        if (query.Date is DateOnly date)
        {
            occupiedRoomIds = await OccupiedRoomIdsAsync(date, cancellationToken);
            var board = query.Status ?? RoomBoardFilter.All;
            q = board switch
            {
                RoomBoardFilter.Maintenance => q.Where(r => r.Status == RoomStatus.Maintenance),
                RoomBoardFilter.Inactive => q.Where(r => r.Status == RoomStatus.Inactive),
                RoomBoardFilter.Occupied => q.Where(r => r.Status != RoomStatus.Maintenance && r.Status != RoomStatus.Inactive
                    && occupiedRoomIds.Contains(r.Id)),
                RoomBoardFilter.Available => q.Where(r => r.Status == RoomStatus.Available && !occupiedRoomIds.Contains(r.Id)),
                _ => q
            };
        }
        else if (query.Status is RoomBoardFilter board)
        {
            var status = MapBoardFilter(board);
            if (status is not null)
                q = q.Where(r => r.Status == status);
        }

        var joined = q.Join(db.RoomTypes, r => r.RoomTypeId, rt => rt.Id,
            (r, rt) => new { Room = r, RoomTypeName = rt.Name });

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim();
            joined = joined.Where(x => x.Room.RoomNumber.Contains(search) || x.RoomTypeName.Contains(search));
        }

        var total = await joined.CountAsync(cancellationToken);

        var descending = query.IsDescending;
        var ordered = query.Sort?.Trim().ToLowerInvariant() switch
        {
            "status" => Order(joined, x => x.Room.Status, descending),
            "floor" => Order(joined, x => x.Room.Floor, descending),
            "roomtypename" => Order(joined, x => x.RoomTypeName, descending),
            _ => Order(joined, x => x.Room.RoomNumber, descending)
        };

        var rows = await ordered
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(x => new { x.Room.Id, x.Room.RoomNumber, x.Room.RoomTypeId, x.RoomTypeName, x.Room.Status, x.Room.Floor })
            .ToListAsync(cancellationToken);

        var items = rows
            .Select(x => new RoomListItemResponse(
                x.Id, x.RoomNumber, x.RoomTypeId, x.RoomTypeName, x.Status.ToString(), x.Floor,
                EffectiveStatus(x.Status, x.Id, query.Date, occupiedRoomIds)))
            .ToList();

        return new PaginatedResult<RoomListItemResponse>(items, total, query.Page, query.PageSize);

        static IOrderedQueryable<T> Order<T, TKey>(IQueryable<T> source, Expression<Func<T, TKey>> key, bool descending) =>
            descending ? source.OrderByDescending(key) : source.OrderBy(key);
    }

    private async Task<List<Guid>> OccupiedRoomIdsAsync(DateOnly date, CancellationToken cancellationToken) =>
        await db.RoomAssignments
            .Where(ra => ra.ReleasedAt == null)
            .Join(db.Bookings.Where(b => (b.Status == BookingStatus.Confirmed || b.Status == BookingStatus.CheckedIn)
                    && b.CheckInDate <= date && b.CheckOutDate > date),
                ra => ra.BookingId, b => b.Id, (ra, b) => ra.RoomId)
            .Distinct()
            .ToListAsync(cancellationToken);

    private static string EffectiveStatus(RoomStatus physical, Guid roomId, DateOnly? date, List<Guid>? occupied)
    {
        if (date is null || physical is RoomStatus.Maintenance or RoomStatus.Inactive)
            return physical.ToString();
        return occupied!.Contains(roomId) ? nameof(RoomStatus.Occupied) : nameof(RoomStatus.Available);
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

    private static RoomStatus? MapBoardFilter(RoomBoardFilter filter) => filter switch
    {
        RoomBoardFilter.Available => RoomStatus.Available,
        RoomBoardFilter.Occupied => RoomStatus.Occupied,
        RoomBoardFilter.Maintenance => RoomStatus.Maintenance,
        RoomBoardFilter.Inactive => RoomStatus.Inactive,
        _ => null
    };
}