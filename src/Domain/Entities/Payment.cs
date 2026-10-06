using Deutchezz.Domain.Common;
using Deutchezz.Domain.Enums;

namespace Deutchezz.Domain.Entities;

public class Payment : AuditableEntity
{
    public Guid BookingId { get; set; }
    public decimal Amount { get; set; }
    public PaymentMethod Method { get; set; }
    public string? Reference { get; set; }
    public Guid? RecordedByUserId { get; set; }
    public DateTime? PaidAt { get; set; }
}