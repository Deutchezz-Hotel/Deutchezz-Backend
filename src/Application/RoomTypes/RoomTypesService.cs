using Deutchezz.Application.Abstractions;
using Deutchezz.Application.Common;
using Deutchezz.Application.Data;
using Deutchezz.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace Deutchezz.Application.RoomTypes;

public class RoomTypesService(IAppDbContext db, IActivityLogger activityLogger) : IRoomTypesService
{
    public async Task<Result<PaginatedResult<RoomTypeResponse>>> ListAsync(RoomTypeQuery query, CancellationToken cancellationToken)
    {
        var q = db.RoomTypes.AsQueryable();
        if (!query.IncludeInactive)
            q = q.Where(rt => rt.IsActive);

        if (!string.IsNullOrWhiteSpace(query.Name))
            q = q.Where(rt => rt.Name.ToLower().Contains(query.Name.ToLower()));

        var total = await q.CountAsync(cancellationToken);
        var items = await q
            .OrderBy(rt => rt.NightlyRate)
            .Skip((query.PageNumber - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(rt => new RoomTypeResponse(rt.Id, rt.Name, rt.Description, rt.NightlyRate, rt.MaxOccupancy, rt.IsActive))
            .ToListAsync(cancellationToken);

        return new PaginatedResult<RoomTypeResponse>(items, total, query.PageNumber, query.PageSize);
    }

    public async Task<Result<RoomTypeResponse>> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        var roomType = await db.RoomTypes.FirstOrDefaultAsync(rt => rt.Id == id && rt.IsActive, cancellationToken);
        if (roomType is null)
            return Error.NotFound("roomtype.not_found", "Room type not found");

        return new RoomTypeResponse(roomType.Id, roomType.Name, roomType.Description, roomType.NightlyRate, roomType.MaxOccupancy, roomType.IsActive);
    }

    public async Task<Result<RoomTypeResponse>> CreateAsync(CreateRoomTypeRequest request, Guid adminUserId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return Error.Validation("roomtype.name_required", "Name is required");
        if (request.NightlyRate <= 0)
            return Error.Validation("roomtype.invalid_rate", "Nightly rate must be positive");
        if (request.MaxOccupancy < 1)
            return Error.Validation("roomtype.invalid_occupancy", "Maximum occupancy must be at least 1");

        var duplicate = await db.RoomTypes.FirstOrDefaultAsync(rt => rt.Name == request.Name, cancellationToken);
        if (duplicate is not null)
            return Error.Conflict("roomtype.name_in_use", "A room type with that name already exists");

        var roomType = new Domain.Entities.RoomType
        {
            Name = request.Name,
            Description = request.Description,
            NightlyRate = request.NightlyRate,
            MaxOccupancy = request.MaxOccupancy,
            IsActive = true
        };
        db.RoomTypes.Add(roomType);
        await db.SaveChangesAsync(cancellationToken);

        await activityLogger.LogAsync(adminUserId, "roomtype.created", "RoomType", roomType.Id,
            $"Created room type '{roomType.Name}' at {roomType.NightlyRate}", cancellationToken);

        return new RoomTypeResponse(roomType.Id, roomType.Name, roomType.Description, roomType.NightlyRate, roomType.MaxOccupancy, roomType.IsActive);
    }

    public async Task<Result<RoomTypeResponse>> UpdateAsync(Guid id, UpdateRoomTypeRequest request, Guid adminUserId, CancellationToken cancellationToken)
    {
        var roomType = await db.RoomTypes.FirstOrDefaultAsync(rt => rt.Id == id, cancellationToken);
        if (roomType is null)
            return Error.NotFound("roomtype.not_found", "Room type not found");

        if (request.Name is not null)
        {
            if (string.IsNullOrWhiteSpace(request.Name))
                return Error.Validation("roomtype.name_required", "Name cannot be empty");
            var duplicate = await db.RoomTypes.FirstOrDefaultAsync(rt => rt.Name == request.Name && rt.Id != id, cancellationToken);
            if (duplicate is not null)
                return Error.Conflict("roomtype.name_in_use", "A room type with that name already exists");
            roomType.Name = request.Name;
        }

        if (request.Description is not null)
            roomType.Description = request.Description;

        // Changing a rate never touches existing BookingRoomType snapshots.
        if (request.NightlyRate is > 0)
            roomType.NightlyRate = request.NightlyRate.Value;

        if (request.MaxOccupancy is > 0)
            roomType.MaxOccupancy = request.MaxOccupancy.Value;

        await db.SaveChangesAsync(cancellationToken);

        await activityLogger.LogAsync(adminUserId, "roomtype.updated", "RoomType", roomType.Id, "Updated room type", cancellationToken);

        return new RoomTypeResponse(roomType.Id, roomType.Name, roomType.Description, roomType.NightlyRate, roomType.MaxOccupancy, roomType.IsActive);
    }

    public async Task<Result> SetActiveAsync(Guid id, bool isActive, Guid adminUserId, CancellationToken cancellationToken)
    {
        var roomType = await db.RoomTypes.FirstOrDefaultAsync(rt => rt.Id == id, cancellationToken);
        if (roomType is null)
            return Error.NotFound("roomtype.not_found", "Room type not found");

        // Deactivating a type with future bookings is rejected.
        if (!isActive)
        {
            var hasFutureBookings = await db.BookingRoomTypes
                .Where(brt => brt.RoomTypeId == id)
                .Join(db.Bookings.Where(b => b.CheckInDate > DateOnly.FromDateTime(DateTime.UtcNow)
                        && (b.Status == Domain.Enums.BookingStatus.Confirmed || b.Status == Domain.Enums.BookingStatus.CheckedIn)),
                    brt => brt.BookingId, b => b.Id, (brt, b) => b.Id)
                .AnyAsync(cancellationToken);

            if (hasFutureBookings)
                return Error.Conflict("roomtype.has_future_bookings", "Cannot deactivate a room type with future bookings");
        }

        roomType.IsActive = isActive;
        await db.SaveChangesAsync(cancellationToken);

        await activityLogger.LogAsync(adminUserId, "roomtype.status_changed", "RoomType", roomType.Id,
            $"Set active = {isActive}", cancellationToken);

        return Result.Success();
    }
}