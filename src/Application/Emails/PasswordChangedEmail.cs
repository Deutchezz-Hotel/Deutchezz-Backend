namespace Deutchezz.Application.Emails;

public sealed record PasswordChangedModel(string GuestName, DateTime ChangedAtUtc);

/// <summary>Sent after a successful password reset as a security notice.</summary>
public static class PasswordChangedEmail
{
    public static (string Subject, string Html, string Text) Build(PasswordChangedModel model)
    {
        var subject = "Your Deutchezz password was changed";
        var name = System.Net.WebUtility.HtmlEncode(model.GuestName);
        var when = model.ChangedAtUtc.ToString("u");

        var body = $"""
            <p style="margin:0 0 16px;">Hello {name},</p>
            <p style="margin:0 0 16px;">Your Deutchezz account password was changed successfully.</p>
            <p style="margin:0 0 16px;">If you did not make this change, contact the hotel immediately so we can secure your account.</p>
            <p style="margin:0;color:#6B5B4B;font-size:13px;">All other signed-in sessions have been signed out.</p>
            """;

        var text = EmailLayout.PlainText("Your password was changed",
            $"Hello {model.GuestName},",
            $"Your password was changed at {when} UTC.",
            "If you did not make this change, contact the hotel immediately.",
            "All other signed-in sessions have been signed out.");

        return (subject, EmailLayout.Render("Your password was changed", body,
            "Your Deutchezz password was changed."), text);
    }
}
