namespace Deutchezz.Application.Emails;

public sealed record BookingCancellationModel(
    string BookingReference,
    string GuestName,
    DateOnly CheckInDate,
    DateOnly CheckOutDate,
    string? Reason,
    decimal? RefundedAmount);

/// <summary>Sent when a booking is cancelled (registered customer or anonymous).</summary>
public static class BookingCancellationEmail
{
    public static (string Subject, string Html, string Text) Build(BookingCancellationModel model)
    {
        var subject = $"Booking cancelled {model.BookingReference}";
        var reason = string.IsNullOrWhiteSpace(model.Reason) ? null : System.Net.WebUtility.HtmlEncode(model.Reason);
        var refund = model.RefundedAmount;

        var body = $"""
            <p style="margin:0 0 16px;">Dear {System.Net.WebUtility.HtmlEncode(model.GuestName)},</p>
            <p style="margin:0 0 16px;">Your booking has been cancelled. If this was not you, please contact us immediately.</p>
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="background:#F7F1E8;border-radius:6px;margin-bottom:18px;">
              <tr><td style="padding:14px 16px;">
                <div style="font-size:12px;letter-spacing:1px;text-transform:uppercase;color:#4A2C1A;">Booking reference</div>
                <div style="font-size:22px;font-weight:700;color:#4A2C1A;">{System.Net.WebUtility.HtmlEncode(model.BookingReference)}</div>
              </td></tr>
            </table>
            <p style="margin:0 0 6px;"><strong>Check-in:</strong> {model.CheckInDate:yyyy-MM-dd}</p>
            <p style="margin:0 0 6px;"><strong>Check-out:</strong> {model.CheckOutDate:yyyy-MM-dd}</p>
            {(refund is not null ? $"<p style=\"margin:12px 0 0;\"><strong>Refunded:</strong> NGN {refund:N2}</p>" : "")}
            {(reason is null ? "" : $"<p style=\"margin:12px 0 0;\"><strong>Reason:</strong> {reason}</p>")}
            """;

        var text = EmailLayout.PlainText("Booking cancelled",
            $"Booking reference: {model.BookingReference}",
            $"Guest: {model.GuestName}",
            $"Check-in: {model.CheckInDate:yyyy-MM-dd}",
            $"Check-out: {model.CheckOutDate:yyyy-MM-dd}",
            refund is not null ? $"Refunded: NGN {refund:N2}" : "",
            reason is null ? "" : $"Reason: {model.Reason}");

        return (subject, EmailLayout.Render("Booking cancelled", body,
            $"Your booking {model.BookingReference} has been cancelled."), text);
    }
}
