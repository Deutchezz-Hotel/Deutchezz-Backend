namespace Deutchezz.Application.Emails;

public sealed record EmailVerificationModel(string GuestName, string Code, int ExpiryMinutes);

/// <summary>Sent on registration with the 6-digit code that creates the account (v1.6).</summary>
public static class EmailVerificationEmail
{
    public static (string Subject, string Html, string Text) Build(EmailVerificationModel model)
    {
        var subject = "Your Deutchezz verification code";
        var code = System.Net.WebUtility.HtmlEncode(model.Code);

        var body = $"""
            <p style="margin:0 0 16px;">Welcome to Deutchezz Hotel Ispahan, {System.Net.WebUtility.HtmlEncode(model.GuestName)}.</p>
            <p style="margin:0 0 16px;">Enter this code to finish creating your account:</p>
            <p style="margin:24px 0;font-size:32px;font-weight:700;letter-spacing:6px;color:#4A2C1A;">{code}</p>
            <p style="margin:0 0 16px;color:#6B5B4B;font-size:13px;">
              The code expires in {model.ExpiryMinutes} minutes. If you did not create an account, you can ignore this email.
            </p>
            """;

        var text = EmailLayout.PlainText("Your verification code",
            $"Welcome, {model.GuestName}.",
            $"Your verification code is {model.Code}.",
            $"The code expires in {model.ExpiryMinutes} minutes.",
            "If you did not create an account, you can ignore this email.");

        return (subject, EmailLayout.Render("Verify your email", body,
            "Your Deutchezz verification code is inside."), text);
    }
}
