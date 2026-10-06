namespace Deutchezz.Application.Availability;

public record RoomTypeAvailabilityResponse(
    Guid RoomTypeId,
    string Name,
    decimal NightlyRate,
    int MaxOccupancy,
    int Capacity,
    int Booked,
    int Available);

public record AvailabilityResponse(DateOnly CheckInDate, DateOnly CheckOutDate, List<RoomTypeAvailabilityResponse> RoomTypes);

public record CalendarDayResponse(DateOnly Date, int Capacity, int Booked, int Available);

public record CalendarResponse(Guid RoomTypeId, int Year, int Month, List<CalendarDayResponse> Days);