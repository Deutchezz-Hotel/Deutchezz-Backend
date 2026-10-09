using Deutchezz.Application.Abstractions;
using Deutchezz.Application.Common;
using Deutchezz.Application.Data;
using Deutchezz.Domain.Common;
using Deutchezz.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Deutchezz.Application.Rooms;

public class RoomsService(IAppDbContext db, IActivityLogger activityLogger) : IRoomsService
{
    public async Task<Result<PaginatedResult<RoomResponse>>> ListAsync(RoomQuery query, CancellationToken cancellationToken)
    {
        var q = db.Rooms.AsQueryable();

        if (query.Status is RoomBoardFilter board)
        {
            var status = MapBoardFilter(board);
            if (status is not null)
                q = q.Where(r => r.Status == status);
        }

        if (query.RoomTypeId is not null)
            q = q.Where(r => r.RoomTypeId == query.RoomTypeId);

        var total = await q.CountAsync(cancellationToken);

        var queryable = await q
            .Join(db.RoomTypes, r => r.RoomTypeId, rt => rt.Id,
                (r, rt) => new RoomResponse(r.Id, r.RoomNumber, r.RoomTypeId, rt.Name, r.Status.ToString(), r.Floor, r.IsActive))
            .OrderBy(r => r.RoomNumber)
            .Skip((query.PageNumber - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(cancellationToken);

        return new PaginatedResult<RoomResponse>(queryable, total, query.PageNumber, query.PageSize);
    }

    public async Task<Result<RoomResponse>> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        var room = await db.Rooms
            .Join(db.RoomTypes, r => r.RoomTypeId, rt => rt.Id,
                (r, rt) => new { Room = r, RTypeName = rt.Name })
            .FirstOrDefaultAsync(x => x.Room.Id == id, cancellationToken);

        if (room is null)
            return Error.NotFound("room.not_found", "Room not found");

        return new RoomResponse(room.Room.Id, room.Room.RoomNumber, room.Room.RoomTypeId, room.RTypeName, room.Room.Status.ToString(), room.Room.Floor, room.Room.IsActive);
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