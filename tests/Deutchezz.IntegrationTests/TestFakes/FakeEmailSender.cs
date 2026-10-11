using Deutchezz.Application.Abstractions;

namespace Deutchezz.IntegrationTests.TestFakes;

public class FakeEmailSender : IEmailSender
{
    private readonly List<SentEmail> _sent = new();

    public IReadOnlyList<SentEmail> Sent => _sent;

    public Task SendAsync(string to, string subject, string htmlBody, string? textBody = null, CancellationToken cancellationToken = default)
    {
        _sent.Add(new SentEmail(to, subject, htmlBody));
        return Task.CompletedTask;
    }

    public record SentEmail(string To, string Subject, string HtmlBody);
}
