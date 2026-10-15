using System.Linq.Expressions;
using Deutchezz.Application.Abstractions;
using Deutchezz.Application.Common;
using Deutchezz.Application.Data;
using Deutchezz.Application.Stays;
using Deutchezz.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace Deutchezz.Application.RoomTypes;

public class RoomTypesService(IAppDbContext db, IActivityLogger activityLogger) : IRoomTypesService
{
    private const int MaxImageUrls = 8;
    private const int MaxFeatures = 20;

    private static readonly Dictionary<string, Expression<Func<Domain.Entities.RoomType, object>>> Sortable =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["name"] = rt => rt.Name,
            ["basePrice"] = rt => rt.BasePrice,
            ["maximumOccupancy"] = rt => rt.MaximumOccupancy,
            ["isActive"] = rt => rt.IsActive,
            ["createdAt"] = rt => rt.CreatedAt
        };

    public async Task<Result<PaginatedResult<RoomTypeListItemResponse>>> ListAsync(RoomTypeQuery query, CancellationToken cancellationToken)
    {
        var q = db.RoomTypes.AsQueryable();
        if (!query.IncludeInactive)
            q = q.Where(rt => rt.IsActive);

        if (!string.IsNullOrWhiteSpace(query.Name))
            q = q.Where(rt => rt.Name.ToLower().Contains(query.Name.ToLower()));

        var total = await q.CountAsync(cancellationToken);
        var items = await q
            .ApplySort(query, Sortable, qq => qq.OrderBy(rt => rt.BasePrice))
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(rt => new RoomTypeListItemResponse(rt.Id, rt.Name, rt.BasePrice, rt.MaximumOccupancy, rt.BedType, rt.IsActive))
            .ToListAsync(cancellationToken);

        return new PaginatedResult<RoomTypeListItemResponse>(items, total, query.Page, query.PageSize);
    }

    public async Task<Result<RoomTypeResponse>> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        var roomType = await db.RoomTypes.FirstOrDefaultAsync(rt => rt.Id == id && rt.IsActive, cancellationToken);
        if (roomType is null)
            return Error.NotFound("roomtype.not_found", "Room type not found");

        return ToResponse(roomType);
    }

    public async Task<Result<RoomTypeResponse>> GetByIdForAdminAsync(Guid id, CancellationToken cancellationToken)
    {
        // Admins must be able to load (and therefore edit) inactive types too; the public
        // GetByIdAsync deliberately hides them.
        var roomType = await db.RoomTypes.FirstOrDefaultAsync(rt => rt.Id == id, cancellationToken);
        if (roomType is null)
            return Error.NotFound("roomtype.not_found", "Room type not found");

        return ToResponse(roomType);
    }

    public async Task<Result<RoomTypeResponse>> CreateAsync(CreateRoomTypeRequest request, Guid adminUserId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return Error.Validation("roomtype.name_required", "Name is required");
        if (request.BasePrice <= 0)
            return Error.Validation("roomtype.invalid_rate", "Base price must be positive");
        if (request.MaximumOccupancy < 1)
            return Error.Validation("roomtype.invalid_occupancy", "Maximum occupancy must be at least 1");

        var imageUrlsValidated = ValidateImageUrls(request.ImageUrls);
        var featuresValidated = ValidateFeatures(request.Features);
        if (imageUrlsValidated is not null)
            return imageUrlsValidated;
        if (featuresValidated is not null)
            return featuresValidated;

        var duplicate = await db.RoomTypes.FirstOrDefaultAsync(rt => rt.Name == request.Name, cancellationToken);
        if (duplicate is not null)
            return Error.Conflict("roomtype.name_in_use", "A room type with that name already exists");

        var roomType = new Domain.Entities.RoomType
        {
            Name = request.Name,
            Description = request.Description,
            BasePrice = request.BasePrice,
            MaximumOccupancy = request.MaximumOccupancy,
            BedType = request.BedType,
            ImageUrls = request.ImageUrls ?? [],
            Features = request.Features ?? [],
            IsActive = true
        };
        db.RoomTypes.Add(roomType);

        // v1.14: every new room type starts with the default late-check-out tier table so a
        // late check-out is priced the same way as a seeded type (otherwise any overage would
        // fall back to a full night). Admins can adjust the amounts later.
        foreach (var tier in StayPricing.DefaultTiersFor(roomType.Name))
        {
            db.RoomTypeSurchargeTiers.Add(new Domain.Entities.RoomTypeSurchargeTier
            {
                RoomTypeId = roomType.Id,
                FromHours = tier.FromHours,
                ToHours = tier.ToHours,
                Amount = tier.Amount
            });
        }

        await db.SaveChangesAsync(cancellationToken);

        await activityLogger.LogAsync(adminUserId, "roomtype.created", "RoomType", roomType.Id,
            $"Created room type '{roomType.Name}' at {roomType.BasePrice}", cancellationToken);

        return ToResponse(roomType);
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
        if (request.BasePrice is > 0)
            roomType.BasePrice = request.BasePrice.Value;

        if (request.MaximumOccupancy is > 0)
            roomType.MaximumOccupancy = request.MaximumOccupancy.Value;

        if (request.BedType is not null)
            roomType.BedType = request.BedType;

        if (request.ImageUrls is not null)
        {
            var imageUrlsValidated = ValidateImageUrls(request.ImageUrls);
            if (imageUrlsValidated is not null)
                return imageUrlsValidated;
            roomType.ImageUrls = request.ImageUrls;
        }

        if (request.Features is not null)
        {
            var featuresValidated = ValidateFeatures(request.Features);
            if (featuresValidated is not null)
                return featuresValidated;
            roomType.Features = request.Features;
        }

        await db.SaveChangesAsync(cancellationToken);

        await activityLogger.LogAsync(adminUserId, "roomtype.updated", "RoomType", roomType.Id, "Updated room type", cancellationToken);

        return ToResponse(roomType);
    }

    public async Task<Result<RoomTypeResponse>> SetActiveAsync(Guid id, bool isActive, Guid adminUserId, CancellationToken cancellationToken)
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
                        && (b.Status == Domain.Enums.BookingStatus.Pending
                            || b.Status == Domain.Enums.BookingStatus.Confirmed
                            || b.Status == Domain.Enums.BookingStatus.CheckedIn)),
                    brt => brt.BookingId, b => b.Id, (brt, b) => b.Id)
                .AnyAsync(cancellationToken);

            if (hasFutureBookings)
                return Error.Conflict("roomtype.has_future_bookings", "Cannot deactivate a room type with future bookings");
        }

        roomType.IsActive = isActive;
        await db.SaveChangesAsync(cancellationToken);

        await activityLogger.LogAsync(adminUserId, "roomtype.status_changed", "RoomType", roomType.Id,
            $"Set active = {isActive}", cancellationToken);

        // Return the updated representation so the client can trust the server state (and never
        // has to map an empty 204 body, which previously surfaced as a bogus "Network error").
        return ToResponse(roomType);
    }

    private static Error? ValidateImageUrls(List<string>? imageUrls)
    {
        if (imageUrls is null)
            return null;
        if (imageUrls.Count > MaxImageUrls)
            return Error.Validation("roomtype.too_many_images", $"A room type can have at most {MaxImageUrls} image URLs");
        if (imageUrls.Any(u => !Uri.TryCreate(u, UriKind.Absolute, out var parsed) || (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps)))
            return Error.Validation("roomtype.invalid_image_url", "Image URLs must be absolute http/https URLs");

        return null;
    }

    private static Error? ValidateFeatures(List<string>? features)
    {
        if (features is null)
            return null;
        if (features.Count > MaxFeatures)
            return Error.Validation("roomtype.too_many_features", $"A room type can have at most {MaxFeatures} features");
        if (features.Any(f => string.IsNullOrWhiteSpace(f)))
            return Error.Validation("roomtype.invalid_feature", "Features must be non-empty");

        return null;
    }

    private static RoomTypeResponse ToResponse(Domain.Entities.RoomType rt) =>
        new(rt.Id, rt.Name, rt.Description, rt.BasePrice, rt.MaximumOccupancy, rt.BedType, rt.IsActive,
            rt.ImageUrls, rt.Features);
}