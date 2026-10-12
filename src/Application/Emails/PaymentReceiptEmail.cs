namespace Deutchezz.Application.Emails;

public sealed record PaymentReceiptModel(
    string BookingReference,
    string GuestName,
    decimal Amount,
    string Method,
    string? Reference,
    decimal TotalAmount,
    decimal TotalPaid,
    decimal Balance,
    DateTime RecordedAt);

/// <summary>Sent when a payment is recorded against a booking.</summary>
public static class PaymentReceiptEmail
{
    public static (string Subject, string Html, string Text) Build(PaymentReceiptModel model)
    {
        var subject = $"Payment received for {model.BookingReference}";
        var reference = string.IsNullOrWhiteSpace(model.Reference) ? "—" : System.Net.WebUtility.HtmlEncode(model.Reference);

        var body = $"""
            <p style="margin:0 0 16px;">Dear {System.Net.WebUtility.HtmlEncode(model.GuestName)},</p>
            <p style="margin:0 0 16px;">We have received your payment. Thank you.</p>
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="background:#F7F1E8;border-radius:6px;margin-bottom:18px;">
              <tr><td style="padding:14px 16px;">
                <div style="font-size:12px;letter-spacing:1px;text-transform:uppercase;color:#4A2C1A;">Amount received</div>
                <div style="font-size:22px;font-weight:700;color:#4A2C1A;">NGN {model.Amount:N2}</div>
              </td>
              <td style="padding:14px 16px;text-align:right;">
                <div style="font-size:12px;letter-spacing:1px;text-transform:uppercase;color:#4A2C1A;">Booking</div>
                <div style="font-size:15px;font-weight:600;color:#4A2C1A;">{System.Net.WebUtility.HtmlEncode(model.BookingReference)}</div>
              </td></tr>
            </table>
            <p style="margin:0 0 6px;"><strong>Method:</strong> {System.Net.WebUtility.HtmlEncode(model.Method)}</p>
            <p style="margin:0 0 6px;"><strong>Reference:</strong> {reference}</p>
            <p style="margin:0 0 6px;"><strong>Recorded:</strong> {model.RecordedAt:yyyy-MM-dd HH:mm}</p>
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="margin-top:14px;border-top:1px solid #E4DAC8;">
              <tr><td style="padding:8px 0;">Booking total</td><td style="padding:8px 0;text-align:right;">NGN {model.TotalAmount:N2}</td></tr>
              <tr><td style="padding:8px 0;">Paid to date</td><td style="padding:8px 0;text-align:right;">NGN {model.TotalPaid:N2}</td></tr>
              <tr><td style="padding:8px 0;font-weight:700;color:#4A2C1A;">Balance due</td><td style="padding:8px 0;text-align:right;font-weight:700;color:#4A2C1A;">NGN {model.Balance:N2}</td></tr>
            </table>
            """;

        var text = EmailLayout.PlainText("Payment received",
            $"Booking reference: {model.BookingReference}",
            $"Guest: {model.GuestName}",
            $"Amount: NGN {model.Amount:N2}",
            $"Method: {model.Method}",
            $"Reference: {model.Reference ?? "-"}",
            $"Total: NGN {model.TotalAmount:N2} / Paid: NGN {model.TotalPaid:N2} / Balance: NGN {model.Balance:N2}");

        return (subject, EmailLayout.Render("Payment received", body,
            $"We received NGN {model.Amount:N2} for booking {model.BookingReference}."), text);
    }
}
