namespace Deutchezz.Application.Emails;

public sealed record EmailVerificationModel(string GuestName, string VerifyUrl);

/// <summary>Sent on registration to confirm the account's email address.</summary>
public static class EmailVerificationEmail
{
    public static (string Subject, string Html, string Text) Build(EmailVerificationModel model)
    {
        var subject = "Confirm your email address";
        var url = System.Net.WebUtility.HtmlEncode(model.VerifyUrl);

        var body = $"""
            <p style="margin:0 0 16px;">Welcome to Deutchezz Hotel Ispahan, {System.Net.WebUtility.HtmlEncode(model.GuestName)}.</p>
            <p style="margin:0 0 16px;">Please confirm your email address so we can send you booking updates.</p>
            <p style="margin:24px 0;">
              <a href="{url}" style="background:#4A2C1A;color:#F7F1E8;text-decoration:none;padding:12px 22px;border-radius:6px;font-weight:600;display:inline-block;">Confirm email</a>
            </p>
            <p style="margin:0 0 16px;color:#6B5B4B;font-size:13px;">If you did not create an account, you can ignore this email.</p>
            <p style="margin:0;color:#6B5B4B;font-size:12px;word-break:break-all;">{url}</p>
            """;

        var text = EmailLayout.PlainText("Confirm your email address",
            $"Welcome, {model.GuestName}.",
            "Open this link to confirm your email address:",
            model.VerifyUrl);

        return (subject, EmailLayout.Render("Confirm your email", body), text);
    }
}
