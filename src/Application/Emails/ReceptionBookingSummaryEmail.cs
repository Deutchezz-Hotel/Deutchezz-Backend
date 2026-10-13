namespace Deutchezz.Application.Emails;

public sealed record ReceptionBookingSummaryModel(
    string BookingReference,
    string GuestName,
    string GuestPhone,
    DateOnly CheckInDate,
    DateOnly CheckOutDate,
    int Nights,
    IReadOnlyList<BookingConfirmationRoom> Rooms,
    decimal TotalAmount);

/// <summary>
/// Sent to the creating staff member after a walk-in booking. Walk-in guests have no email, so the
/// hotel keeps the only summary (SoT 7g, BR-036).
/// </summary>
public static class ReceptionBookingSummaryEmail
{
    public static (string Subject, string Html, string Text) Build(ReceptionBookingSummaryModel model)
    {
        var subject = $"Walk-in booking {model.BookingReference}";

        var body = $"""
            <p style="margin:0 0 16px;">A walk-in booking has been created for the following guest.</p>
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="background:#F7F1E8;border-radius:6px;margin-bottom:20px;">
              <tr>
                <td style="padding:14px 16px;">
                  <div style="font-size:12px;letter-spacing:1px;text-transform:uppercase;color:#4A2C1A;">Booking reference</div>
                  <div style="font-size:22px;font-weight:700;color:#4A2C1A;">{System.Net.WebUtility.HtmlEncode(model.BookingReference)}</div>
                </td>
                <td style="padding:14px 16px;text-align:right;">
                  <div style="font-size:12px;letter-spacing:1px;text-transform:uppercase;color:#4A2C1A;">Payment</div>
                  <div style="font-size:15px;font-weight:600;color:#4A2C1A;">Pay at hotel on arrival</div>
                </td>
              </tr>
            </table>
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="margin-bottom:18px;">
              <tr><td style="padding:6px 0;"><strong style="color:#4A2C1A;">Guest:</strong> {System.Net.WebUtility.HtmlEncode(model.GuestName)}</td></tr>
              <tr><td style="padding:6px 0;"><strong style="color:#4A2C1A;">Phone:</strong> {System.Net.WebUtility.HtmlEncode(model.GuestPhone)}</td></tr>
              <tr><td style="padding:6px 0;"><strong style="color:#4A2C1A;">Check-in:</strong> {model.CheckInDate:yyyy-MM-dd}</td></tr>
              <tr><td style="padding:6px 0;"><strong style="color:#4A2C1A;">Check-out:</strong> {model.CheckOutDate:yyyy-MM-dd}</td></tr>
              <tr><td style="padding:6px 0;"><strong style="color:#4A2C1A;">Nights:</strong> {model.Nights}</td></tr>
            </table>
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="border-collapse:collapse;margin-bottom:18px;">
              {EmailLayout.RoomLines(model.Rooms)}
              {EmailLayout.TotalRow("Total", model.TotalAmount)}
            </table>
            <p style="margin:0;color:#6B5B4B;font-size:13px;">
              Walk-in guests do not receive emails. Keep this summary for your records.
            </p>
            """;

        var text = EmailLayout.PlainText("Walk-in booking",
            $"Booking reference: {model.BookingReference}",
            $"Guest: {model.GuestName}",
            $"Phone: {model.GuestPhone}",
            $"Check-in: {model.CheckInDate:yyyy-MM-dd}",
            $"Check-out: {model.CheckOutDate:yyyy-MM-dd}",
            $"Nights: {model.Nights}",
            $"Rooms: {string.Join(", ", model.Rooms.Select(r => $"{r.RoomTypeName} x{r.Quantity} @ {r.RatePerNight:N0}/night"))}",
            $"Total: NGN {model.TotalAmount:N2}",
            "Payment: Pay at hotel on arrival.");

        return (subject, EmailLayout.Render("Walk-in booking", body,
            $"Walk-in booking {model.BookingReference} recorded."), text);
    }
}
