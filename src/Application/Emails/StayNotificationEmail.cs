namespace Deutchezz.Application.Emails;

public sealed record StayNotificationModel(
    string BookingReference,
    string GuestName,
    IReadOnlyList<string> RoomNumbers,
    DateTime OccurredAt);

/// <summary>Sent when a booking is checked in or checked out.</summary>
public static class StayNotificationEmail
{
    public static (string Subject, string Html, string Text) BuildCheckIn(StayNotificationModel model)
    {
        var rooms = System.Net.WebUtility.HtmlEncode(string.Join(", ", model.RoomNumbers));
        var body = $"""
            <p style="margin:0 0 16px;">Dear {System.Net.WebUtility.HtmlEncode(model.GuestName)},</p>
            <p style="margin:0 0 16px;">You are checked in. We wish you a pleasant stay at Deutchezz.</p>
            <p style="margin:0 0 6px;"><strong>Booking:</strong> {System.Net.WebUtility.HtmlEncode(model.BookingReference)}</p>
            <p style="margin:0 0 6px;"><strong>Room(s):</strong> {rooms}</p>
            <p style="margin:0 0 6px;"><strong>Checked in:</strong> {model.OccurredAt:yyyy-MM-dd HH:mm}</p>
            """;
        var text = EmailLayout.PlainText("Checked in",
            $"Booking reference: {model.BookingReference}",
            $"Guest: {model.GuestName}",
            $"Room(s): {string.Join(", ", model.RoomNumbers)}",
            $"Checked in: {model.OccurredAt:yyyy-MM-dd HH:mm}");
        return ($"Checked in {model.BookingReference}", EmailLayout.Render("Checked in", body), text);
    }

    public static (string Subject, string Html, string Text) BuildCheckOut(StayNotificationModel model)
    {
        var rooms = System.Net.WebUtility.HtmlEncode(string.Join(", ", model.RoomNumbers));
        var body = $"""
            <p style="margin:0 0 16px;">Dear {System.Net.WebUtility.HtmlEncode(model.GuestName)},</p>
            <p style="margin:0 0 16px;">You are checked out. Thank you for staying with us — we hope to welcome you again.</p>
            <p style="margin:0 0 6px;"><strong>Booking:</strong> {System.Net.WebUtility.HtmlEncode(model.BookingReference)}</p>
            <p style="margin:0 0 6px;"><strong>Room(s):</strong> {rooms}</p>
            <p style="margin:0 0 6px;"><strong>Checked out:</strong> {model.OccurredAt:yyyy-MM-dd HH:mm}</p>
            """;
        var text = EmailLayout.PlainText("Checked out",
            $"Booking reference: {model.BookingReference}",
            $"Guest: {model.GuestName}",
            $"Room(s): {string.Join(", ", model.RoomNumbers)}",
            $"Checked out: {model.OccurredAt:yyyy-MM-dd HH:mm}");
        return ($"Checked out {model.BookingReference}", EmailLayout.Render("Checked out", body), text);
    }
}
