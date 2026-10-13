namespace Deutchezz.Application.Emails;

public sealed record StaffNewBookingAlertModel(
    string BookingReference,
    string GuestName,
    string GuestEmail,
    string GuestPhone,
    DateOnly CheckInDate,
    TimeOnly CheckInTime,
    DateOnly CheckOutDate,
    TimeOnly CheckOutTime,
    int Nights,
    IReadOnlyList<BookingConfirmationRoom> Rooms,
    decimal TotalAmount);

/// <summary>Staff-facing alert sent to the front desk inbox for every new booking.</summary>
public static class StaffNewBookingAlertEmail
{
    public static (string Subject, string Html, string Text) Build(StaffNewBookingAlertModel model)
    {
        var subject = $"New booking {model.BookingReference} — {model.CheckInDate:yyyy-MM-dd}";

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
            <p style="margin:0 0 6px;">{Deutchezz.Application.Stays.StayFormat.ScheduleLine("Check-in", model.CheckInDate, model.CheckInTime, true)}</p>
            <p style="margin:0 0 6px;">{Deutchezz.Application.Stays.StayFormat.ScheduleLine("Check-out", model.CheckOutDate, model.CheckOutTime, false)} ({model.Nights} night(s))</p>
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="border-collapse:collapse;margin:16px 0;">
              {EmailLayout.RoomLines(model.Rooms, showRate: false)}
              {EmailLayout.TotalRow("Total", model.TotalAmount)}
            </table>
            """;

        var text = EmailLayout.PlainText("New booking received",
            $"Booking reference: {model.BookingReference}",
            $"Guest: {model.GuestName} <{model.GuestEmail}> {model.GuestPhone}",
            Deutchezz.Application.Stays.StayFormat.ScheduleLine("Check-in", model.CheckInDate, model.CheckInTime, true),
            Deutchezz.Application.Stays.StayFormat.ScheduleLine("Check-out", model.CheckOutDate, model.CheckOutTime, false),
            $"Nights: {model.Nights}",
            $"Rooms: {string.Join(", ", model.Rooms.Select(r => $"{r.RoomTypeName} x{r.Quantity}"))}",
            $"Total: NGN {model.TotalAmount:N2}");

        return (subject, EmailLayout.Render("New booking received", body,
            $"New booking {model.BookingReference} for {model.GuestName}."), text);
    }
}
