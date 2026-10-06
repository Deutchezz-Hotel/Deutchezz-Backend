namespace Deutchezz.Backend.Application.DTOs.Availability;

public class AvailabilitySearchRequest
{
    public DateTime CheckInDate { get; set; }
    public DateTime CheckOutDate { get; set; }
    public int Adults { get; set; }
    public int Children { get; set; }
    public int Rooms { get; set; }
}

public class AvailableRoomTypeResponse
{
    public Guid RoomTypeId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal BasePrice { get; set; }
    public int MaximumOccupancy { get; set; }
    public int AvailableQuantity { get; set; }
}

public class AvailabilityResponse
{
    public List<AvailableRoomTypeResponse> RoomTypes { get; set; } = new();
}