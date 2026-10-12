namespace Deutchezz.Application.Emails;

public sealed record ExistingAccountModel(string GuestName);

/// <summary>
/// Sent when someone tries to register with an email address that already has an account. Keeps
/// the HTTP response identical to a fresh registration (BR-028) while telling the real owner.
/// </summary>
public static class ExistingAccountEmail
{
    public static (string Subject, string Html, string Text) Build(ExistingAccountModel model)
    {
        var subject = "You already have a Deutchezz account";
        var name = System.Net.WebUtility.HtmlEncode(model.GuestName);

        var body = $"""
            <p style="margin:0 0 16px;">Hello {name},</p>
            <p style="margin:0 0 16px;">Someone tried to create an account with this email address, but one already exists.</p>
            <p style="margin:0 0 16px;">If that was you, sign in instead. If you have forgotten your password, use "Forgot password" on the sign-in page.</p>
            <p style="margin:0;color:#6B5B4B;font-size:13px;">If this was not you, you can ignore this email.</p>
            """;

        var text = EmailLayout.PlainText("You already have an account",
            $"Hello {model.GuestName},",
            "Someone tried to create an account with this email address, but one already exists.",
            "Sign in instead, or use \"Forgot password\" if you need to reset it.",
            "If this was not you, you can ignore this email.");

        return (subject, EmailLayout.Render("You already have an account", body,
            "This email address already has a Deutchezz account."), text);
    }
}
