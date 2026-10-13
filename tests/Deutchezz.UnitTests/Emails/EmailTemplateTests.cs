using System.Text.RegularExpressions;
using Deutchezz.Application.Emails;

namespace Deutchezz.UnitTests.Emails;

/// <summary>
/// v1.10 email hardening (SoT 13): every template renders a responsive, table-based shell
/// (viewport + color-scheme, &lt;=600px card, 16px padding), a plain-text alternative, HTML-encoded
/// dynamic values, and two-cell line-item rows. Renders HTML previews to
/// <c>docs/email-previews/*.html</c> for manual inspection (the docs folder is git-ignored).
/// </summary>
public class EmailTemplateTests
{
    private static readonly BookingConfirmationRoom[] Rooms =
    [
        new("Standard Room", 2, 50000m, 100000m),
        new("Deluxe Suite", 1, 120000m, 120000m)
    ];

    public static IEnumerable<object[]> AllTemplates()
    {
        const string Ref = "DHI-2026-ABC234";
        var date = new DateOnly(2026, 11, 3);
        yield return Row("booking-confirmation",
            BookingConfirmationEmail.Build(new BookingConfirmationModel(Ref, "Ada Obi", date, date.AddDays(2), 2, new TimeOnly(14, 0), Rooms, 220000m)));
        yield return Row("booking-updated",
            BookingUpdatedEmail.Build(new BookingUpdatedModel(Ref, "Ada Obi", date, date.AddDays(2), 2, Rooms, 220000m)));
        yield return Row("booking-cancellation",
            BookingCancellationEmail.Build(new BookingCancellationModel(Ref, "Ada Obi", date, date.AddDays(2), "GuestRequest", 50000m)));
        yield return Row("payment-receipt",
            PaymentReceiptEmail.Build(new PaymentReceiptModel(Ref, "Ada Obi", 50000m, "Cash", "RC-1", 220000m, 70000m, 150000m, new DateTime(2026, 11, 1, 9, 30, 0, DateTimeKind.Utc))));
        yield return Row("checked-in",
            StayNotificationEmail.BuildCheckIn(new StayNotificationModel(Ref, "Ada Obi", ["101", "102"], new DateTime(2026, 11, 3, 14, 5, 0, DateTimeKind.Utc))));
        yield return Row("checked-out",
            StayNotificationEmail.BuildCheckOut(new StayNotificationModel(Ref, "Ada Obi", ["101", "102"], new DateTime(2026, 11, 5, 11, 0, 0, DateTimeKind.Utc))));
        yield return Row("email-verification",
            EmailVerificationEmail.Build(new EmailVerificationModel("Ada Obi", "123456", 15)));
        yield return Row("existing-account",
            ExistingAccountEmail.Build(new ExistingAccountModel("Ada Obi")));
        yield return Row("password-reset",
            PasswordResetEmail.Build(new PasswordResetModel("Ada Obi", "654321", 15)));
        yield return Row("password-changed",
            PasswordChangedEmail.Build(new PasswordChangedModel("Ada Obi", new DateTime(2026, 11, 1, 9, 30, 0, DateTimeKind.Utc))));
        yield return Row("role-changed",
            RoleChangedEmail.Build(new RoleChangedModel("Ada Obi", "Receptionist")));
        yield return Row("reception-booking-summary",
            ReceptionBookingSummaryEmail.Build(new ReceptionBookingSummaryModel(Ref, "Walk In", "+2348031234567", date, date.AddDays(2), 2, Rooms, 220000m)));
        yield return Row("staff-new-booking-alert",
            StaffNewBookingAlertEmail.Build(new StaffNewBookingAlertModel(Ref, "Ada Obi", "ada@example.com", "+2348031234567", date, date.AddDays(2), 2, Rooms, 220000m)));
    }

    private static object[] Row(string slug, (string Subject, string Html, string Text) email) =>
        [slug, email.Subject, email.Html, email.Text];

    [Theory]
    [MemberData(nameof(AllTemplates))]
    public void Every_template_is_responsive_has_plain_text_and_a_preheader(string slug, string subject, string html, string text)
    {
        Assert.False(string.IsNullOrWhiteSpace(subject));

        // Responsive shell.
        Assert.Contains("name=\"viewport\"", html);
        Assert.Contains("width=device-width", html);
        Assert.Contains("name=\"color-scheme\"", html);
        Assert.Contains("max-width:600px", html);
        Assert.Contains("width=\"100%\"", html);
        Assert.Contains("<meta charset=", html);

        // Hidden preheader (a sneaky preview line shown next to the subject).
        Assert.Contains("mso-hide:all", html);

        // Plain-text alternative, genuinely tag-free (angle brackets in "<email@...>" are fine).
        Assert.False(string.IsNullOrWhiteSpace(text), $"{slug} has no plain-text part");
        Assert.DoesNotContain("<html", text);
        Assert.DoesNotContain("<body", text);
        Assert.DoesNotContain("<table", text);
        Assert.DoesNotContain("<div", text);
        Assert.DoesNotContain("<p ", text);

        WritePreview(slug, html, text);
    }

    [Fact]
    public void Guest_supplied_html_is_encoded()
    {
        // Rebuild with a hostile guest name by using the confirmation template as the probe.
        var host = BookingConfirmationEmail.Build(new BookingConfirmationModel(
            "DHI-2026-ABC234", "<script>alert('x')</script>", new DateOnly(2026, 11, 3), new DateOnly(2026, 11, 5),
            2, new TimeOnly(14, 0), Rooms, 220000m));

        Assert.DoesNotContain("<script", host.Html);
        Assert.Contains("&lt;script&gt;", host.Html);
    }

    [Fact]
    public void Room_line_items_use_at_most_two_cells_per_row()
    {
        var html = EmailLayout.RoomLines(Rooms);

        foreach (Match row in Regex.Matches(html, "<tr[^>]*>(.*?)</tr>", RegexOptions.Singleline))
        {
            var cells = Regex.Matches(row.Groups[1].Value, "<(td|th)[ >]").Count;
            Assert.True(cells <= 2, $"A room line-item row had {cells} cells: {row.Value}");
        }

        // No remnants of the old three-column room table.
        Assert.DoesNotContain("Rate / night (NGN)", html);
        Assert.DoesNotContain("Subtotal (NGN)", html);
    }

    [Fact]
    public void Booking_templates_carry_the_reference_and_total()
    {
        var (_, html, text) = BookingConfirmationEmail.Build(new BookingConfirmationModel(
            "DHI-2026-XYZ999", "Ada Obi", new DateOnly(2026, 11, 3), new DateOnly(2026, 11, 5),
            2, new TimeOnly(14, 0), Rooms, 220000m));

        Assert.Contains("DHI-2026-XYZ999", html);
        Assert.Contains("DHI-2026-XYZ999", text);
        Assert.Contains("Total", html);
    }

    private static void WritePreview(string slug, string html, string text)
    {
        var root = FindRepoRoot();
        var dir = Path.Combine(root, "docs", "email-previews");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, $"{slug}.html"), html);
        File.WriteAllText(Path.Combine(dir, $"{slug}.txt"), text);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "src"))
                && Directory.Exists(Path.Combine(dir.FullName, "tests")))
                return dir.FullName;
            dir = dir.Parent!;
        }

        throw new InvalidOperationException("Could not locate the repository root.");
    }
}
