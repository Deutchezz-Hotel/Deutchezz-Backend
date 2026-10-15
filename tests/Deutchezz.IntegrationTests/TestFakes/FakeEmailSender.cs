using System.Text.RegularExpressions;
using Deutchezz.Application.Abstractions;

namespace Deutchezz.IntegrationTests.TestFakes;

public class FakeEmailSender : IEmailSender
{
    private readonly List<SentEmail> _sent = new();

    public IReadOnlyList<SentEmail> Sent => _sent;

    /// <summary>Most recent email sent to <paramref name="to"/> (or null).</summary>
    public SentEmail? LatestFor(string to) => _sent.LastOrDefault(e => e.To == to);

    /// <summary>Extracts the 6-digit code from the latest email sent to <paramref name="to"/>.</summary>
    public string? CodeFor(string to)
    {
        var email = LatestFor(to);
        if (email is null)
            return null;

        var match = Regex.Match(email.HtmlBody, @"\b(\d{6})\b");
        return match.Success ? match.Groups[1].Value : null;
    }

    public Task SendAsync(string to, string subject, string htmlBody, string? textBody = null, CancellationToken cancellationToken = default, string? replyTo = null)
    {
        _sent.Add(new SentEmail(to, subject, htmlBody, textBody, replyTo));
        return Task.CompletedTask;
    }

    public record SentEmail(string To, string Subject, string HtmlBody, string? TextBody = null, string? ReplyTo = null);
}
