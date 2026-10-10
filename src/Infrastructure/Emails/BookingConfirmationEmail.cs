namespace Deutchezz.Infrastructure.Emails;

public sealed record BookingConfirmationRoom(string RoomTypeName, int Quantity, decimal RatePerNight, decimal Subtotal);

public sealed record BookingConfirmationModel(
    string BookingReference,
    string GuestName,
    DateOnly CheckInDate,
    DateOnly CheckOutDate,
    int Nights,
    TimeOnly CheckInTime,
    IReadOnlyList<BookingConfirmationRoom> Rooms,
    decimal TotalAmount);

/// <summary>
/// Booking confirmation HTML. Brand colours: brown #4A2C1A, milk #F7F1E8.
/// </summary>
public static class BookingConfirmationEmail
{
    private const string Brown = "#4A2C1A";
    private const string Milk = "#F7F1E8";

    public static (string Subject, string Html, string Text) Build(BookingConfirmationModel model)
    {
        var subject = $"Booking received {model.BookingReference}";

        var rowBuilder = new System.Text.StringBuilder();
        foreach (var r in model.Rooms)
        {
            rowBuilder.Append("<tr>")
                .Append($"<td style=\"padding:8px;border-bottom:1px solid #E4DAC8;\">{System.Net.WebUtility.HtmlEncode(r.RoomTypeName)} &times; {r.Quantity}</td>")
                .Append($"<td style=\"padding:8px;border-bottom:1px solid #E4DAC8;text-align:right;\">{r.RatePerNight:N0}</td>")
                .Append($"<td style=\"padding:8px;border-bottom:1px solid #E4DAC8;text-align:right;\">{r.Subtotal:N2}</td>")
                .Append("</tr>");
        }
        var rows = rowBuilder.ToString();

        var html = $"""
            <!doctype html>
            <html><body style="margin:0;background:#F5F5F5;font-family:Segoe UI,Roboto,Helvetica,Arial,sans-serif;">
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="background:{Milk};padding:24px 0;">
              <tr><td align="center">
                <table role="presentation" width="600" cellpadding="0" cellspacing="0"
                       style="background:#FFFFFF;border:1px solid #E4DAC8;border-radius:8px;overflow:hidden;">
                  <tr>
                    <td style="background:{Brown};color:{Milk};padding:20px 24px;">
                      <div style="font-size:20px;font-weight:600;">Deutchezz Hotel Ispahan</div>
                      <div style="font-size:13px;opacity:0.9;">Ahmadu Bello Way, Jos, Plateau State, Nigeria</div>
                    </td>
                  </tr>
                  <tr><td style="padding:24px;">
                    <p style="margin:0 0 16px;">Dear {System.Net.WebUtility.HtmlEncode(model.GuestName)},</p>
                    <p style="margin:0 0 16px;">Your booking is confirmed. We look forward to welcoming you.</p>

                    <table role="presentation" width="100%" cellpadding="0" cellspacing="0"
                           style="background:{Milk};border-radius:6px;margin-bottom:20px;">
                      <tr>
                        <td style="padding:14px 16px;">
                          <div style="font-size:12px;letter-spacing:1px;text-transform:uppercase;color:{Brown};">Booking reference</div>
                          <div style="font-size:22px;font-weight:700;color:{Brown};">{model.BookingReference}</div>
                        </td>
                        <td style="padding:14px 16px;text-align:right;">
                          <div style="font-size:12px;letter-spacing:1px;text-transform:uppercase;color:{Brown};">Payment</div>
                          <div style="font-size:15px;font-weight:600;color:{Brown};">Pay at hotel on arrival</div>
                        </td>
                      </tr>
                    </table>

                    <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="margin-bottom:18px;">
                      <tr>
                        <td style="padding:6px 0;"><strong style="color:{Brown};">Check-in:</strong> {model.CheckInDate:yyyy-MM-dd} from {model.CheckInTime:HH\:mm}</td>
                      </tr>
                      <tr>
                        <td style="padding:6px 0;"><strong style="color:{Brown};">Check-out:</strong> {model.CheckOutDate:yyyy-MM-dd}</td>
                      </tr>
                      <tr>
                        <td style="padding:6px 0;"><strong style="color:{Brown};">Nights:</strong> {model.Nights}</td>
                      </tr>
                    </table>

                    <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="border-collapse:collapse;margin-bottom:18px;">
                      <tr style="background:{Brown};color:{Milk};">
                        <th style="padding:8px;text-align:left;">Rooms</th>
                        <th style="padding:8px;text-align:right;">Rate / night (NGN)</th>
                        <th style="padding:8px;text-align:right;">Subtotal (NGN)</th>
                      </tr>
                      {rows}
                      <tr>
                        <td colspan="2" style="padding:12px 8px;text-align:right;font-weight:700;color:{Brown};">Total</td>
                        <td style="padding:12px 8px;text-align:right;font-weight:700;color:{Brown};">{model.TotalAmount:N2}</td>
                      </tr>
                    </table>

                    <p style="margin:0;color:#6B5B4B;font-size:13px;">
                      Check-in opens at {model.CheckInTime:HH\:mm} on {model.CheckInDate:yyyy-MM-dd}.
                      Please present this reference at reception.
                    </p>
                  </td></tr>
                  <tr><td style="background:{Milk};color:#6B5B4B;padding:14px 24px;font-size:12px;">
                    Deutchezz Hotel Ispahan · Ahmadu Bello Way, Jos, Plateau State, Nigeria
                  </td></tr>
                </table>
              </td></tr>
            </table>
            </body></html>
            """;

        var text = $"""
            Deutchezz Hotel Ispahan — Ahmadu Bello Way, Jos, Plateau State, Nigeria

            Booking reference: {model.BookingReference}
            Guest: {model.GuestName}
            Check-in: {model.CheckInDate:yyyy-MM-dd} from {model.CheckInTime:HH\:mm}
            Check-out: {model.CheckOutDate:yyyy-MM-dd}
            Nights: {model.Nights}
            Rooms: {string.Join(", ", model.Rooms.Select(r => $"{r.RoomTypeName} x{r.Quantity} @ {r.RatePerNight:N0}/night"))}
            Total: NGN {model.TotalAmount:N2}
            Payment: Pay at hotel on arrival.
            """;

        return (subject, html, text);
    }
}