using Deutchezz.Backend.Domain.Common;
using Deutchezz.Backend.Domain.Enums;

namespace Deutchezz.Backend.Domain.Entities;

public class Booking : AuditableEntity
{
    public string BookingReference { get; set; } = string.Empty;
    public Guid GuestId { get; set; }
    public Guest? Guest { get; set; }
    public DateTime CheckInDate { get; set; }
    public DateTime CheckOutDate { get; set; }
    public int Adults { get; set; }
    public int Children { get; set; }
    public BookingStatus Status { get; set; } = BookingStatus.Pending;
    public PaymentStatus PaymentStatus { get; set; } = PaymentStatus.Pending;
    public string? SpecialRequests { get; set; }
    public decimal TotalAmount { get; set; }
    public ICollection<BookingRoomType> BookingRoomTypes { get; set; } = new List<BookingRoomType>();
    public ICollection<RoomAssignment> RoomAssignments { get; set; } = new List<RoomAssignment>();
    public ICollection<Payment> Payments { get; set; } = new List<Payment>();
}