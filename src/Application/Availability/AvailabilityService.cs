using Deutchezz.Application.Abstractions;
using Deutchezz.Application.Data;
using Deutchezz.Application.Stays;
using Deutchezz.Domain.Common;
using Deutchezz.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Deutchezz.Application.Availability;

public class AvailabilityService(
    IAppDbContext db, IStayPolicyService stays, IStayConflictService conflicts, IBusinessClock businessClock)
    : IAvailabilityService
{
    public async Task<Result<AvailabilityResponse>> SearchAsync(AvailabilitySearchRequest request, CancellationToken cancellationToken)
    {
        if (request.Rooms < 1)
            return Error.Validation("availability.invalid_rooms", "At least one room is required");
        if (request.Adults + request.Children < 1)
            return Error.Validation("availability.invalid_guests", "At least one guest is required");

        var stay = await stays.ResolveAsync(
            request.CheckInDate, request.CheckInTime, request.CheckOutDate, request.CheckOutTime, false, cancellationToken);
        if (stay.IsFailure)
            return stay.Error.Code == "stay.invalid_range"
                ? Error.Validation("availability.invalid_range", stay.Error.Description)
                : stay.Error;
        var s = stay.Value;

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

            var booked = await conflicts.CountOverlappingQuantityAsync(
                roomType.Id, null, s.CheckInAt, s.CheckOutAt, s.Policy.TurnaroundMinutes, cancellationToken);

            var available = Math.Max(0, capacity - booked);
            if (available < request.Rooms)
                continue;

            result.Add(new AvailableRoomTypeResponse(
                roomType.Id, roomType.Name, roomType.BasePrice, roomType.MaximumOccupancy,
                capacity, booked, available));
        }

        var lagosOffset = businessClock.ConvertFromUtc(businessClock.UtcNow).Offset;
        return new AvailabilityResponse(
            s.CheckInDate, Format(s.CheckInTime), s.CheckOutDate, Format(s.CheckOutTime),
            StayFormat.Iso(s.CheckInAt, lagosOffset), StayFormat.Iso(s.CheckOutAt, lagosOffset), result);
    }

    private static string Format(TimeOnly time) => time.ToString("HH:mm");
}