using Deutchezz.Backend.Domain.Common;
using Deutchezz.Backend.Domain.Enums;

namespace Deutchezz.Backend.Domain.Entities;

public class Payment : BaseEntity
{
    public Guid BookingId { get; set; }
    public Booking? Booking { get; set; }
    public decimal Amount { get; set; }
    public PaymentMethod PaymentMethod { get; set; }
    public PaymentStatus Status { get; set; } = PaymentStatus.Pending;
    public string? Reference { get; set; }
    public DateTime? PaidAt { get; set; }
    public Guid? RecordedByUserId { get; set; }
    public User? RecordedByUser { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}