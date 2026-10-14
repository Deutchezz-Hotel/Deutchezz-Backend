namespace Deutchezz.Application.Emails;

public sealed record BookingConfirmationRoom(
    string RoomTypeName,
    int Quantity,
    decimal RatePerNight,
    decimal Subtotal,
    decimal ExtraCharge = 0m);

public sealed record BookingConfirmationModel(
    string BookingReference,
    string GuestName,
    DateOnly CheckInDate,
    TimeOnly CheckInTime,
    DateOnly CheckOutDate,
    TimeOnly CheckOutTime,
    int Nights,
    IReadOnlyList<BookingConfirmationRoom> Rooms,
    decimal TotalAmount);

/// <summary>Booking confirmation HTML.</summary>
public static class BookingConfirmationEmail
{
    public static (string Subject, string Html, string Text) Build(BookingConfirmationModel model)
    {
        var subject = $"Booking received {model.BookingReference}";

        var body = $"""
            <p style="margin:0 0 16px;">Dear {System.Net.WebUtility.HtmlEncode(model.GuestName)},</p>
            <p style="margin:0 0 16px;">Your booking is confirmed. We look forward to welcoming you.</p>
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
              <tr>
                <td style="padding:6px 0;">{Deutchezz.Application.Stays.StayFormat.ScheduleLine("Check-in", model.CheckInDate, model.CheckInTime, true)}</td>
              </tr>
              <tr>
                <td style="padding:6px 0;">{Deutchezz.Application.Stays.StayFormat.ScheduleLine("Check-out", model.CheckOutDate, model.CheckOutTime, false)}</td>
              </tr>
              <tr><td style="padding:6px 0;"><strong style="color:#4A2C1A;">Nights:</strong> {model.Nights}</td></tr>
            </table>
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="border-collapse:collapse;margin-bottom:18px;">
              {EmailLayout.RoomLines(model.Rooms)}
              {EmailLayout.TotalRow("Total", model.TotalAmount)}
            </table>
            <p style="margin:0;color:#6B5B4B;font-size:13px;">
              {Deutchezz.Application.Stays.StayFormat.ScheduleLine("Check-in", model.CheckInDate, model.CheckInTime, true)}.
              Please present this reference at reception.
            </p>
            """;

        var text = EmailLayout.PlainText("Booking received",
            $"Booking reference: {model.BookingReference}",
            $"Guest: {model.GuestName}",
            Deutchezz.Application.Stays.StayFormat.ScheduleLine("Check-in", model.CheckInDate, model.CheckInTime, true),
            Deutchezz.Application.Stays.StayFormat.ScheduleLine("Check-out", model.CheckOutDate, model.CheckOutTime, false),
            $"Nights: {model.Nights}",
            $"Rooms: {string.Join(", ", model.Rooms.Select(r => $"{r.RoomTypeName} x{r.Quantity} @ {r.RatePerNight:N0}/night"))}",
            $"Total: NGN {model.TotalAmount:N2}",
            "Payment: Pay at hotel on arrival.");

        return (subject, EmailLayout.Render("Booking confirmed", body,
            $"Your booking {model.BookingReference} is confirmed."), text);
    }
}
