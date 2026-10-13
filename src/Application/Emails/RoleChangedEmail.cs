namespace Deutchezz.Application.Emails;

public sealed record RoleChangedModel(string GuestName, string Role);

/// <summary>Sent when an administrator changes a user's role.</summary>
public static class RoleChangedEmail
{
    public static (string Subject, string Html, string Text) Build(RoleChangedModel model)
    {
        var subject = "Your Deutchezz account role has changed";
        var name = System.Net.WebUtility.HtmlEncode(model.GuestName);
        var role = System.Net.WebUtility.HtmlEncode(model.Role);

        var body = $"""
            <p style="margin:0 0 16px;">Hello {name},</p>
            <p style="margin:0 0 16px;">An administrator has changed your Deutchezz account role to <strong>{role}</strong>.</p>
            <p style="margin:0 0 16px;">Sign out and sign in again to pick up the change. If you did not expect this, contact the hotel immediately.</p>
            """;

        var text = EmailLayout.PlainText("Your account role has changed",
            $"Hello {model.GuestName},",
            $"Your role is now {model.Role}.",
            "Sign out and sign in again to pick up the change.",
            "If you did not expect this, contact the hotel immediately.");

        return (subject, EmailLayout.Render("Your account role has changed", body,
            $"Your Deutchezz account role is now {model.Role}."), text);
    }
}
