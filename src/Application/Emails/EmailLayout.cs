using System.Net;
using System.Text;

namespace Deutchezz.Application.Emails;

/// <summary>
/// Shared branded shell for transactional emails. Brand colours: brown #4A2C1A, milk #F7F1E8.
/// Kept in the Application layer because application services compose the emails; only the
/// transport (SMTP) lives in Infrastructure.
///
/// v1.10: the shell is table-based and mobile-friendly — a 100%-wide outer table with a
/// <c>max-width:600px</c> inner card, 16px side padding, a 16px base font, a hidden preheader and
/// a viewport/color-scheme declaration. A small <c>&lt;style&gt;</c> block only tweaks the few
/// classes that need to re-flow on narrow screens; every critical rule is also inlined so it
/// degrades gracefully in clients that strip styles.
/// </summary>
public static class EmailLayout
{
    private const string Brown = "#4A2C1A";
    private const string Milk = "#F7F1E8";

    public static string Render(string heading, string bodyHtml, string? preheader = null)
    {
        var encodedHeading = WebUtility.HtmlEncode(heading);
        var pre = WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(preheader) ? heading : preheader);

        return $$"""
            <!doctype html>
            <html lang="en">
            <head>
            <meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <meta name="x-apple-disable-message-reformatting">
            <meta name="color-scheme" content="light">
            <meta name="supported-color-schemes" content="light">
            <title>{{encodedHeading}}</title>
            <style>
              body { margin:0; padding:0; }
              table { border-collapse:collapse; }
              .container { width:100%; max-width:600px; }
              @media (max-width:600px) {
                .stack { display:block !important; width:100% !important; }
                .amount { text-align:left !important; }
              }
            </style>
            </head>
            <body style="margin:0;padding:0;background:#F5F5F5;font-family:Segoe UI,Roboto,Helvetica,Arial,sans-serif;font-size:16px;line-height:1.5;color:#2B2B2B;">
            <span style="display:none !important;visibility:hidden;opacity:0;color:transparent;height:0;width:0;overflow:hidden;mso-hide:all;">{{pre}}</span>
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="background:{{Milk}};">
              <tr><td align="center" style="padding:24px 16px;background:{{Milk}};">
                <table role="presentation" class="container" width="100%" cellpadding="0" cellspacing="0"
                       style="max-width:600px;background:#FFFFFF;border:1px solid #E4DAC8;border-radius:8px;overflow:hidden;">
                  <tr>
                    <td style="background:{{Brown}};color:{{Milk}};padding:20px 16px;">
                      <div style="font-size:20px;font-weight:600;">Deutchezz Hotel Ispahan</div>
                      <div style="font-size:13px;">Ahmadu Bello Way, Jos, Plateau State, Nigeria</div>
                    </td>
                  </tr>
                  <tr><td style="padding:24px 16px;">
                    <h1 style="margin:0 0 16px;color:{{Brown}};font-size:22px;line-height:1.3;">{{encodedHeading}}</h1>
                    {{bodyHtml}}
                  </td></tr>
                  <tr><td style="background:{{Milk}};color:#6B5B4B;padding:14px 16px;font-size:12px;">
                    Deutchezz Hotel Ispahan · Ahmadu Bello Way, Jos, Plateau State, Nigeria
                  </td></tr>
                </table>
              </td></tr>
            </table>
            </body></html>
            """;
    }

    public static string PlainText(string heading, params string[] lines) =>
        $"Deutchezz Hotel Ispahan - Ahmadu Bello Way, Jos, Plateau State, Nigeria\n\n{heading}\n\n" +
        string.Join('\n', lines.Where(l => !string.IsNullOrEmpty(l)));

    /// <summary>
    /// Renders the room line items as a two-cell table (description on the left, subtotal on the
    /// right). A two-cell layout stacks safely on narrow screens instead of squeezing a third
    /// numeric column out of view. Every dynamic value is HTML-encoded.
    /// </summary>
    public static string RoomLines(IReadOnlyList<BookingConfirmationRoom> rooms, bool showRate = true)
    {
        var rows = new StringBuilder();
        // Header row (two cells; the amount column right-aligns but re-flows on mobile).
        rows.Append("<tr style=\"border-bottom:2px solid ").Append(Brown).Append(";\">")
            .Append("<th style=\"padding:8px 0;text-align:left;color:").Append(Brown).Append(";\">Rooms</th>")
            .Append("<th class=\"amount\" style=\"padding:8px 0;text-align:right;color:").Append(Brown).Append(";\">Amount (NGN)</th>")
            .Append("</tr>");

        foreach (var r in rooms)
        {
            var name = WebUtility.HtmlEncode(r.RoomTypeName);
            var rate = showRate
                ? $"<div style=\"color:#6B5B4B;font-size:13px;\">NGN {r.RatePerNight:N0} / night</div>"
                : "";
            var extra = r.ExtraCharge > 0
                ? $"<div style=\"color:#6B5B4B;font-size:13px;\">includes NGN {r.ExtraCharge:N0} late check-out</div>"
                : "";
            rows.Append("<tr>")
                .Append("<td style=\"padding:10px 0;border-bottom:1px solid #E4DAC8;\">")
                .Append("<div style=\"font-weight:600;color:").Append(Brown).Append(";\">")
                .Append(name).Append(" &times; ").Append(r.Quantity).Append("</div>")
                .Append(rate)
                .Append(extra)
                .Append("</td>")
                .Append("<td class=\"amount\" style=\"padding:10px 0;border-bottom:1px solid #E4DAC8;text-align:right;white-space:nowrap;vertical-align:top;\">")
                .Append((r.Subtotal + r.ExtraCharge).ToString("N2"))
                .Append("</td>")
                .Append("</tr>");
        }

        return rows.ToString();
    }

    /// <summary>The bold "Total"/label row that closes a <see cref="RoomLines"/> table.</summary>
    public static string TotalRow(string label, decimal amount) =>
        $"<tr><td style=\"padding:12px 0;text-align:right;font-weight:700;color:{Brown};\">{WebUtility.HtmlEncode(label)}</td>" +
        $"<td class=\"amount\" style=\"padding:12px 0;text-align:right;font-weight:700;color:{Brown};white-space:nowrap;\">NGN {amount:N2}</td></tr>";
}
