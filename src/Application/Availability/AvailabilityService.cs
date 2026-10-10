using Deutchezz.Application.Data;
using Deutchezz.Domain.Common;
using Deutchezz.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Deutchezz.Application.Availability;

public class AvailabilityService(IAppDbContext db) : IAvailabilityService
{
    public async Task<Result<AvailabilityResponse>> SearchAsync(AvailabilitySearchRequest request, CancellationToken cancellationToken)
    {
        if (request.CheckOutDate <= request.CheckInDate)
            return Error.Validation("availability.invalid_range", "Check-out must be after check-in");
        if (request.Rooms < 1)
            return Error.Validation("availability.invalid_rooms", "At least one room is required");
        if (request.Adults + request.Children < 1)
            return Error.Validation("availability.invalid_guests", "At least one guest is required");

        var totalGuests = request.Adults + request.Children;

        var roomTypes = await db.RoomTypes
            .Where(rt => rt.IsActive)
            .OrderBy(rt => rt.BasePrice)
            .ToListAsync(cancellationToken);

        var result = new List<AvailableRoomTypeResponse>();

        foreach (var roomType in roomTypes)
        {
            if ((long)roomType.MaximumOccupancy * request.Rooms < totalGuests)
                continue;

            var capacity = await db.Rooms.CountAsync(
                r => r.RoomTypeId == roomType.Id && r.IsActive
                    && r.Status != RoomStatus.Maintenance && r.Status != RoomStatus.Inactive,
                cancellationToken);

            var booked = await db.BookingRoomTypes
                .Where(brt => brt.RoomTypeId == roomType.Id)
                .Join(db.Bookings.Where(b => (b.Status == BookingStatus.Confirmed || b.Status == BookingStatus.CheckedIn)
                        && b.CheckInDate < request.CheckOutDate && b.CheckOutDate > request.CheckInDate),
                    brt => brt.BookingId,
                    b => b.Id,
                    (brt, b) => brt.Quantity)
                .SumAsync(q => (int?)q, cancellationToken) ?? 0;

            var available = Math.Max(0, capacity - booked);
            if (available < request.Rooms)
                continue;

            result.Add(new AvailableRoomTypeResponse(
                roomType.Id, roomType.Name, roomType.BasePrice, roomType.MaximumOccupancy,
                capacity, booked, available));
        }

        return new AvailabilityResponse(request.CheckInDate, request.CheckOutDate, result);
    }
}