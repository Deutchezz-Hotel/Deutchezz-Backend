namespace Deutchezz.Backend.Application.DTOs.Booking;

public class GuestRequest
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? PhoneNumber { get; set; }
}

public class BookingRoomRequest
{
    public Guid RoomTypeId { get; set; }
    public int Quantity { get; set; }
}

public class CreateBookingRequest
{
    public DateTime CheckInDate { get; set; }
    public DateTime CheckOutDate { get; set; }
    public int Adults { get; set; }
    public int Children { get; set; }
    public GuestRequest Guest { get; set; } = new();
    public List<BookingRoomRequest> Rooms { get; set; } = new();
    public string? SpecialRequests { get; set; }
}

public class BookingLookupRequest
{
    public string BookingReference { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
}