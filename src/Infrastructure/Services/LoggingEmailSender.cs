using Deutchezz.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace Deutchezz.Infrastructure.Services;

/// <summary>
/// Fallback sender used when SMTP credentials are not configured (fresh clone, tests).
/// Logs the message instead of failing.
/// </summary>
public class LoggingEmailSender(ILogger<LoggingEmailSender> logger) : IEmailSender
{
    public Task SendAsync(string to, string subject, string htmlBody, string? textBody = null, CancellationToken cancellationToken = default, string? replyTo = null)
    {
        logger.LogInformation(
            "Email NOT sent (logging fallback) to {To} subject {Subject} replyTo {ReplyTo} htmlLength {Length}",
            to, subject, replyTo ?? "(none)", htmlBody.Length);
        return Task.CompletedTask;
    }
}