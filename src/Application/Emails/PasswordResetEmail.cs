namespace Deutchezz.Application.Emails;

public sealed record PasswordResetModel(string GuestName, string Code, int ExpiryMinutes);

/// <summary>Sent when a user requests a password reset, carrying a 6-digit code (v1.6).</summary>
public static class PasswordResetEmail
{
    public static (string Subject, string Html, string Text) Build(PasswordResetModel model)
    {
        var subject = "Reset your Deutchezz password";
        var code = System.Net.WebUtility.HtmlEncode(model.Code);

        var body = $"""
            <p style="margin:0 0 16px;">Dear {System.Net.WebUtility.HtmlEncode(model.GuestName)},</p>
            <p style="margin:0 0 16px;">We received a request to reset your password. Enter this code to choose a new one:</p>
            <p style="margin:24px 0;font-size:32px;font-weight:700;letter-spacing:6px;color:#4A2C1A;">{code}</p>
            <p style="margin:0 0 16px;color:#6B5B4B;font-size:13px;">
              The code expires in {model.ExpiryMinutes} minutes. If you did not request a password reset, you can safely ignore this email.
            </p>
            """;

        var text = EmailLayout.PlainText("Reset your password",
            $"Dear {model.GuestName},",
            $"Your password reset code is {model.Code}.",
            $"The code expires in {model.ExpiryMinutes} minutes.",
            "If you did not request this, you can ignore this email.");

        return (subject, EmailLayout.Render("Reset your password", body,
            "Use the code inside to reset your Deutchezz password."), text);
    }
}
