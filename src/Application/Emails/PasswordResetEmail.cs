namespace Deutchezz.Application.Emails;

public sealed record PasswordResetModel(string GuestName, string ResetUrl, int ExpiryMinutes);

/// <summary>Sent when a user requests a password reset.</summary>
public static class PasswordResetEmail
{
    public static (string Subject, string Html, string Text) Build(PasswordResetModel model)
    {
        var subject = "Reset your Deutchezz password";
        var url = System.Net.WebUtility.HtmlEncode(model.ResetUrl);

        var body = $"""
            <p style="margin:0 0 16px;">Dear {System.Net.WebUtility.HtmlEncode(model.GuestName)},</p>
            <p style="margin:0 0 16px;">We received a request to reset your password. Click the button below to choose a new one.</p>
            <p style="margin:24px 0;">
              <a href="{url}" style="background:#4A2C1A;color:#F7F1E8;text-decoration:none;padding:12px 22px;border-radius:6px;font-weight:600;display:inline-block;">Reset password</a>
            </p>
            <p style="margin:0 0 16px;color:#6B5B4B;font-size:13px;">
              This link expires in {model.ExpiryMinutes} minutes. If you did not request a password reset, you can safely ignore this email.
            </p>
            <p style="margin:0;color:#6B5B4B;font-size:12px;word-break:break-all;">{url}</p>
            """;

        var text = EmailLayout.PlainText("Reset your password",
            $"Dear {model.GuestName},",
            "Open this link to choose a new password:",
            model.ResetUrl,
            $"This link expires in {model.ExpiryMinutes} minutes.",
            "If you did not request this, you can ignore this email.");

        return (subject, EmailLayout.Render("Reset your password", body), text);
    }
}
