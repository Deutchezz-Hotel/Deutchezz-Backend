namespace Deutchezz.Backend.Application.DTOs.Booking;

public class BookingRoomTypeResponse
{
    public Guid Id { get; set; }
    public Guid RoomTypeId { get; set; }
    public string RoomTypeName { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal RatePerNight { get; set; }
    public int NumberOfNights { get; set; }
    public decimal Subtotal { get; set; }
}

public class PaymentSummaryResponse
{
    public decimal TotalPaid { get; set; }
    public decimal Balance { get; set; }
    public string PaymentStatus { get; set; } = string.Empty;
}

public class BookingResponse
{
    public Guid Id { get; set; }
    public string BookingReference { get; set; } = string.Empty;
    public Guid GuestId { get; set; }
    public string GuestName { get; set; } = string.Empty;
    public string GuestEmail { get; set; } = string.Empty;
    public DateTime CheckInDate { get; set; }
    public DateTime CheckOutDate { get; set; }
    public int Adults { get; set; }
    public int Children { get; set; }
    public string Status { get; set; } = string.Empty;
    public string PaymentStatus { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; }
    public string? SpecialRequests { get; set; }
    public List<BookingRoomTypeResponse> BookingRoomTypes { get; set; } = new();
    public DateTime CreatedAt { get; set; }
}

public class BookingListItemResponse
{
    public Guid Id { get; set; }
    public string BookingReference { get; set; } = string.Empty;
    public string GuestName { get; set; } = string.Empty;
    public DateTime CheckInDate { get; set; }
    public DateTime CheckOutDate { get; set; }
    public string Status { get; set; } = string.Empty;
    public string PaymentStatus { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; }
}

public class BookingDetailResponse : BookingResponse
{
}