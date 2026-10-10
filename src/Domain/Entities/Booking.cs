using Deutchezz.Domain.Common;
using Deutchezz.Domain.Enums;

namespace Deutchezz.Domain.Entities;

public class Booking : AuditableEntity
{
    public string BookingReference { get; set; } = string.Empty;
    public Guid GuestId { get; set; }
    public Guest? Guest { get; set; }
    public DateOnly CheckInDate { get; set; }
    public DateOnly CheckOutDate { get; set; }
    public int Nights { get; set; }
    public int Adults { get; set; }
    public int Children { get; set; }
    public BookingStatus Status { get; set; } = BookingStatus.Pending;
    public PaymentStatus PaymentStatus { get; set; } = PaymentStatus.Pending;
    public string? SpecialRequests { get; set; }
    public decimal TotalAmount { get; set; }
    public DateTime? CancelledAt { get; set; }
    public string? CancellationReason { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public byte[]? ConcurrencyToken { get; set; }
}