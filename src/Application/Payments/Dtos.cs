using Deutchezz.Application.Common;

namespace Deutchezz.Application.Payments;

public record RecordPaymentRequest(Guid BookingId, decimal Amount, string Method, string? Reference);

public record PaymentResponse(Guid Id, Guid BookingId, decimal Amount, string Method, string? Reference, DateTime CreatedAt, Guid? RecordedByUserId);

public class PaymentQuery : QueryParameters
{
    public Guid? BookingId { get; set; }
    public Guid? GuestId { get; set; }
    public DateOnly? From { get; set; }
    public DateOnly? To { get; set; }
}