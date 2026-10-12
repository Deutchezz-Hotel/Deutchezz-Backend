namespace Deutchezz.Application.Emails;

public sealed record StaffNewBookingAlertModel(
    string BookingReference,
    string GuestName,
    string GuestEmail,
    string GuestPhone,
    DateOnly CheckInDate,
    DateOnly CheckOutDate,
    int Nights,
    IReadOnlyList<BookingConfirmationRoom> Rooms,
    decimal TotalAmount);

/// <summary>Staff-facing alert sent to the front desk inbox for every new booking.</summary>
public static class StaffNewBookingAlertEmail
{
    public static (string Subject, string Html, string Text) Build(StaffNewBookingAlertModel model)
    {
        var subject = $"New booking {model.BookingReference} — {model.CheckInDate:yyyy-MM-dd}";

        var rowBuilder = new System.Text.StringBuilder();
        foreach (var r in model.Rooms)
        {
            rowBuilder.Append("<tr>")
                .Append($"<td style=\"padding:8px;border-bottom:1px solid #E4DAC8;\">{System.Net.WebUtility.HtmlEncode(r.RoomTypeName)} &times; {r.Quantity}</td>")
                .Append($"<td style=\"padding:8px;border-bottom:1px solid #E4DAC8;text-align:right;\">NGN {r.Subtotal:N2}</td>")
                .Append("</tr>");
        }
        var rows = rowBuilder.ToString();

        var body = $"""
            <p style="margin:0 0 16px;">A new booking has been created. Please review for room assignment.</p>
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="background:#F7F1E8;border-radius:6px;margin-bottom:18px;">
              <tr><td style="padding:14px 16px;">
                <div style="font-size:12px;letter-spacing:1px;text-transform:uppercase;color:#4A2C1A;">Booking reference</div>
                <div style="font-size:22px;font-weight:700;color:#4A2C1A;">{System.Net.WebUtility.HtmlEncode(model.BookingReference)}</div>
              </td></tr>
            </table>
            <p style="margin:0 0 6px;"><strong>Guest:</strong> {System.Net.WebUtility.HtmlEncode(model.GuestName)}</p>
            <p style="margin:0 0 6px;"><strong>Email:</strong> {System.Net.WebUtility.HtmlEncode(model.GuestEmail)}</p>
            <p style="margin:0 0 6px;"><strong>Phone:</strong> {System.Net.WebUtility.HtmlEncode(model.GuestPhone)}</p>
            <p style="margin:0 0 6px;"><strong>Check-in:</strong> {model.CheckInDate:yyyy-MM-dd}</p>
            <p style="margin:0 0 6px;"><strong>Check-out:</strong> {model.CheckOutDate:yyyy-MM-dd} ({model.Nights} night(s))</p>
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="border-collapse:collapse;margin:16px 0;">
              <tr style="background:#4A2C1A;color:#F7F1E8;">
                <th style="padding:8px;text-align:left;">Rooms</th>
                <th style="padding:8px;text-align:right;">Subtotal (NGN)</th>
              </tr>
              {rows}
              <tr>
                <td style="padding:12px 8px;text-align:right;font-weight:700;color:#4A2C1A;">Total</td>
                <td style="padding:12px 8px;text-align:right;font-weight:700;color:#4A2C1A;">NGN {model.TotalAmount:N2}</td>
              </tr>
            </table>
            """;

        var text = EmailLayout.PlainText("New booking received",
            $"Booking reference: {model.BookingReference}",
            $"Guest: {model.GuestName} <{model.GuestEmail}> {model.GuestPhone}",
            $"Check-in: {model.CheckInDate:yyyy-MM-dd}",
            $"Check-out: {model.CheckOutDate:yyyy-MM-dd}",
            $"Nights: {model.Nights}",
            $"Rooms: {string.Join(", ", model.Rooms.Select(r => $"{r.RoomTypeName} x{r.Quantity}"))}",
            $"Total: NGN {model.TotalAmount:N2}");

        return (subject, EmailLayout.Render("New booking received", body), text);
    }
}
