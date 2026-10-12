namespace Deutchezz.Application.Emails;

public sealed record BookingUpdatedModel(
    string BookingReference,
    string GuestName,
    DateOnly CheckInDate,
    DateOnly CheckOutDate,
    int Nights,
    IReadOnlyList<BookingConfirmationRoom> Rooms,
    decimal TotalAmount);

/// <summary>Booking updated (room type changed) HTML.</summary>
public static class BookingUpdatedEmail
{
    public static (string Subject, string Html, string Text) Build(BookingUpdatedModel model)
    {
        var subject = $"Booking updated {model.BookingReference}";

        var body = $"""
            <p style="margin:0 0 16px;">Dear {System.Net.WebUtility.HtmlEncode(model.GuestName)},</p>
            <p style="margin:0 0 16px;">Your booking has been updated. Here is the latest summary of your stay.</p>
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="background:#F7F1E8;border-radius:6px;margin-bottom:20px;">
              <tr>
                <td style="padding:14px 16px;">
                  <div style="font-size:12px;letter-spacing:1px;text-transform:uppercase;color:#4A2C1A;">Booking reference</div>
                  <div style="font-size:22px;font-weight:700;color:#4A2C1A;">{System.Net.WebUtility.HtmlEncode(model.BookingReference)}</div>
                </td>
                <td style="padding:14px 16px;text-align:right;">
                  <div style="font-size:12px;letter-spacing:1px;text-transform:uppercase;color:#4A2C1A;">Total</div>
                  <div style="font-size:15px;font-weight:600;color:#4A2C1A;">NGN {model.TotalAmount:N2}</div>
                </td>
              </tr>
            </table>
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="margin-bottom:18px;">
              <tr><td style="padding:6px 0;"><strong style="color:#4A2C1A;">Check-in:</strong> {model.CheckInDate:yyyy-MM-dd}</td></tr>
              <tr><td style="padding:6px 0;"><strong style="color:#4A2C1A;">Check-out:</strong> {model.CheckOutDate:yyyy-MM-dd}</td></tr>
              <tr><td style="padding:6px 0;"><strong style="color:#4A2C1A;">Nights:</strong> {model.Nights}</td></tr>
            </table>
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="border-collapse:collapse;margin-bottom:18px;">
              {EmailLayout.RoomLines(model.Rooms)}
              {EmailLayout.TotalRow("Total", model.TotalAmount)}
            </table>
            <p style="margin:0;color:#6B5B4B;font-size:13px;">
              If you have any questions about these changes, please contact our reception.
            </p>
            """;

        var text = EmailLayout.PlainText("Booking updated",
            $"Booking reference: {model.BookingReference}",
            $"Guest: {model.GuestName}",
            $"Check-in: {model.CheckInDate:yyyy-MM-dd}",
            $"Check-out: {model.CheckOutDate:yyyy-MM-dd}",
            $"Nights: {model.Nights}",
            $"Rooms: {string.Join(", ", model.Rooms.Select(r => $"{r.RoomTypeName} x{r.Quantity} @ {r.RatePerNight:N0}/night"))}",
            $"Total: NGN {model.TotalAmount:N2}");

        return (subject, EmailLayout.Render("Booking updated", body,
            $"Your booking {model.BookingReference} has been updated."), text);
    }
}