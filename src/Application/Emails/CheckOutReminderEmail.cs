using System.Net;

namespace Deutchezz.Application.Emails;

public sealed record CheckOutReminderModel(
    string BookingReference,
    string GuestName,
    string? GuestPhone,
    IReadOnlyList<string> RoomNumbers,
    DateOnly CheckOutDate,
    TimeOnly CheckOutTime,
    int MinutesUntilCheckOut);

/// <summary>Staff-facing reminder emailed to the front desk shortly before a checked-in stay ends.</summary>
public static class CheckOutReminderEmail
{
    public static (string Subject, string Html, string Text) Build(CheckOutReminderModel model)
    {
        var reference = WebUtility.HtmlEncode(model.BookingReference);
        var guest = WebUtility.HtmlEncode(model.GuestName);
        var rooms = WebUtility.HtmlEncode(model.RoomNumbers.Count > 0 ? string.Join(", ", model.RoomNumbers) : "none");
        var phone = string.IsNullOrWhiteSpace(model.GuestPhone) ? null : WebUtility.HtmlEncode(model.GuestPhone);
        var minutes = Math.Max(0, model.MinutesUntilCheckOut);
        var subject = $"Check-out due soon: {model.BookingReference} at {model.CheckOutTime:HH\\:mm}";

        var body = $"""
            <p style="margin:0 0 16px;">A checked-in guest is due to check out in about {minutes} minute(s). Please warn the guest and prepare the room for the next arrival.</p>
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="background:#F7F1E8;border-radius:6px;margin-bottom:18px;">
              <tr><td style="padding:14px 16px;">
                <div style="font-size:12px;letter-spacing:1px;text-transform:uppercase;color:#4A2C1A;">Booking reference</div>
                <div style="font-size:22px;font-weight:700;color:#4A2C1A;">{reference}</div>
              </td></tr>
            </table>
            <p style="margin:0 0 6px;"><strong>Guest:</strong> {guest}</p>
            {(phone is null ? string.Empty : $"<p style=\"margin:0 0 6px;\"><strong>Phone:</strong> {phone}</p>")}
            <p style="margin:0 0 6px;"><strong>Room(s):</strong> {rooms}</p>
            <p style="margin:0 0 6px;">{Deutchezz.Application.Stays.StayFormat.ScheduleLine("Check-out", model.CheckOutDate, model.CheckOutTime, false)}</p>
            """;

        var text = EmailLayout.PlainText("Check-out due soon",
            $"Booking reference: {model.BookingReference}",
            $"Guest: {model.GuestName}{(phone is null ? string.Empty : $" ({model.GuestPhone})")}",
            $"Room(s): {string.Join(", ", model.RoomNumbers)}",
            Deutchezz.Application.Stays.StayFormat.ScheduleLine("Check-out", model.CheckOutDate, model.CheckOutTime, false),
            $"Due in about {minutes} minute(s)");

        return (subject, EmailLayout.Render("Check-out due soon", body,
            $"Booking {model.BookingReference} checks out in about {minutes} minute(s)."), text);
    }
}
