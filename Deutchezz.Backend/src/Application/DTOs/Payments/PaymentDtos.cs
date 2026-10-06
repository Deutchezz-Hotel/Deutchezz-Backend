namespace Deutchezz.Backend.Application.DTOs.Payments;

public class RecordPaymentRequest
{
    public Guid BookingId { get; set; }
    public decimal Amount { get; set; }
    public string PaymentMethod { get; set; } = string.Empty;
    public string? Reference { get; set; }
    public string? Notes { get; set; }
}

public class PaymentResponse
{
    public Guid Id { get; set; }
    public Guid BookingId { get; set; }
    public decimal Amount { get; set; }
    public string PaymentMethod { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? Reference { get; set; }
    public DateTime? PaidAt { get; set; }
    public string? Notes { get; set; }
}

public class PaymentListItemResponse : PaymentResponse
{
}