using Deutchezz.Application.Data;
using Deutchezz.Domain.Common;
using Deutchezz.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Deutchezz.Application.Availability;

public class AvailabilityService(IAppDbContext db) : IAvailabilityService
{
    public async Task<Result<AvailabilityResponse>> SearchAsync(DateOnly checkInDate, DateOnly checkOutDate, CancellationToken cancellationToken)
    {
        if (checkOutDate <= checkInDate)
            return Error.Validation("availability.invalid_range", "Check-out must be after check-in");

        var roomTypes = await db.RoomTypes
            .Where(rt => rt.IsActive && !rt.IsDeleted)
            .OrderBy(rt => rt.NightlyRate)
            .ToListAsync(cancellationToken);

        var result = new List<RoomTypeAvailabilityResponse>();

        foreach (var roomType in roomTypes)
        {
            var capacity = await db.Rooms.CountAsync(
                r => r.RoomTypeId == roomType.Id && !r.IsDeleted && r.Status != RoomStatus.OutOfService,
                cancellationToken);

            var booked = await db.BookingRoomTypes
                .Where(brt => brt.RoomTypeId == roomType.Id)
                .Join(db.Bookings.Where(b => (b.Status == BookingStatus.Confirmed || b.Status == BookingStatus.CheckedIn)
                        && b.CheckInDate < checkOutDate && b.CheckOutDate > checkInDate),
                    brt => brt.BookingId,
                    b => b.Id,
                    (brt, b) => brt.Quantity)
                .SumAsync(q => (int?)q, cancellationToken) ?? 0;

            result.Add(new RoomTypeAvailabilityResponse(
                roomType.Id, roomType.Name, roomType.NightlyRate, roomType.MaxOccupancy,
                capacity, booked, Math.Max(0, capacity - booked)));
        }

        return new AvailabilityResponse(checkInDate, checkOutDate, result);
    }

    public async Task<Result<CalendarResponse>> CalendarAsync(Guid roomTypeId, int year, int month, CancellationToken cancellationToken)
    {
        if (month < 1 || month > 12 || year < 2000 || year > 2100)
            return Error.Validation("availability.invalid_month", "Invalid year or month");

        var roomType = await db.RoomTypes.FirstOrDefaultAsync(rt => rt.Id == roomTypeId && !rt.IsDeleted, cancellationToken);
        if (roomType is null)
            return Error.NotFound("roomtype.not_found", "Room type not found");

        var capacity = await db.Rooms.CountAsync(
            r => r.RoomTypeId == roomTypeId && !r.IsDeleted && r.Status != RoomStatus.OutOfService,
            cancellationToken);

        var monthStart = new DateOnly(year, month, 1);
        var monthEnd = monthStart.AddMonths(1);

        var overlaps = await db.BookingRoomTypes
            .Where(brt => brt.RoomTypeId == roomTypeId)
            .Join(db.Bookings.Where(b => (b.Status == BookingStatus.Confirmed || b.Status == BookingStatus.CheckedIn)
                    && b.CheckInDate < monthEnd && b.CheckOutDate > monthStart),
                brt => brt.BookingId,
                b => b.Id,
                (brt, b) => new { b.CheckInDate, b.CheckOutDate, brt.Quantity })
            .ToListAsync(cancellationToken);

        var days = new List<CalendarDayResponse>();
        for (var day = monthStart; day < monthEnd; day = day.AddDays(1))
        {
            var booked = overlaps
                .Where(o => o.CheckInDate < day.AddDays(1) && o.CheckOutDate > day)
                .Sum(o => o.Quantity);
            days.Add(new CalendarDayResponse(day, capacity, booked, Math.Max(0, capacity - booked)));
        }

        return new CalendarResponse(roomTypeId, year, month, days);
    }
}