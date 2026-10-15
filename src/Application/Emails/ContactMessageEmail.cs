namespace Deutchezz.Application.Emails;

public sealed record ContactMessageModel(
    string Name,
    string Email,
    string Message,
    DateTimeOffset ReceivedAt);

/// <summary>
/// Staff-facing email for a message submitted through the public contact form. Sent to the
/// admin notification inbox with the visitor's address set as Reply-To, so the front desk can
/// simply hit Reply to answer.
/// </summary>
public static class ContactMessageEmail
{
    public static (string Subject, string Html, string Text) Build(ContactMessageModel model)
    {
        var name = System.Net.WebUtility.HtmlEncode(model.Name);
        var email = System.Net.WebUtility.HtmlEncode(model.Email);
        var message = System.Net.WebUtility.HtmlEncode(model.Message).Replace("\r\n", "\n").Replace("\n", "<br>");

        var subject = $"Contact form: {model.Name}";

        var body = $"""
            <p style="margin:0 0 16px;">A new message was submitted through the website contact form.</p>
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="background:#F7F1E8;border-radius:6px;margin-bottom:18px;">
              <tr><td style="padding:14px 16px;">
                <div style="font-size:12px;letter-spacing:1px;text-transform:uppercase;color:#4A2C1A;">From</div>
                <div style="font-size:18px;font-weight:700;color:#4A2C1A;">{name}</div>
                <div style="font-size:14px;color:#4A2C1A;">{email}</div>
              </td></tr>
            </table>
            <p style="margin:0 0 6px;"><strong>Message:</strong></p>
            <div style="border-left:3px solid #4A2C1A;padding:4px 0 4px 12px;color:#2B2B2B;">{message}</div>
            """;

        var text = EmailLayout.PlainText("New contact message",
            $"From: {model.Name} <{model.Email}>",
            $"Received: {model.ReceivedAt:u}",
            "",
            model.Message);

        return (subject, EmailLayout.Render("New contact message", body,
            $"New contact message from {model.Name}."), text);
    }
}
