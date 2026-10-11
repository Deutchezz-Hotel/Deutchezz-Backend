using Deutchezz.Application.Common;

namespace Deutchezz.Application.Payments;

public record RecordPaymentRequest(Guid BookingId, decimal Amount, string Method, string? Reference, string? Notes);

public record PaymentResponse(Guid Id, Guid BookingId, decimal Amount, string Method, string Status, string? Reference, string? Notes, DateTime CreatedAt, Guid? RecordedByUserId);

public record PaymentListItemResponse(Guid Id, Guid BookingId, decimal Amount, string Method, string Status, DateTime CreatedAt, string BookingReference, string GuestName, string? Reference);

public class PaymentQuery : QueryParameters
{
    public Guid? BookingId { get; set; }
    public Guid? GuestId { get; set; }
    public DateOnly? From { get; set; }
    public DateOnly? To { get; set; }
}