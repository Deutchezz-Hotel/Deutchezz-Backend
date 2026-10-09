namespace Deutchezz.Application.Availability;

public record AvailabilitySearchRequest(
    DateOnly CheckInDate,
    DateOnly CheckOutDate,
    int Adults,
    int Children,
    int Rooms);

public record AvailableRoomTypeResponse(
    Guid RoomTypeId,
    string Name,
    decimal BasePrice,
    int MaximumOccupancy,
    int Capacity,
    int Booked,
    int Available);

public record AvailabilityResponse(DateOnly CheckInDate, DateOnly CheckOutDate, List<AvailableRoomTypeResponse> RoomTypes);