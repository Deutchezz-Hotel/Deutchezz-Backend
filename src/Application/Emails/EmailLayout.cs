namespace Deutchezz.Application.Emails;

/// <summary>
/// Shared branded shell for transactional emails. Brand colours: brown #4A2C1A, milk #F7F1E8.
/// Kept in the Application layer because application services compose the emails; only the
/// transport (SMTP) lives in Infrastructure.
/// </summary>
public static class EmailLayout
{
    private const string Brown = "#4A2C1A";
    private const string Milk = "#F7F1E8";

    public static string Render(string heading, string bodyHtml) => $"""
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
                <h2 style="margin:0 0 16px;color:{Brown};font-size:20px;">{System.Net.WebUtility.HtmlEncode(heading)}</h2>
                {bodyHtml}
              </td></tr>
              <tr><td style="background:{Milk};color:#6B5B4B;padding:14px 24px;font-size:12px;">
                Deutchezz Hotel Ispahan · Ahmadu Bello Way, Jos, Plateau State, Nigeria
              </td></tr>
            </table>
          </td></tr>
        </table>
        </body></html>
        """;

    public static string PlainText(string heading, params string[] lines) =>
        $"Deutchezz Hotel Ispahan — Ahmadu Bello Way, Jos, Plateau State, Nigeria\n\n{heading}\n\n" +
        string.Join('\n', lines);
}
