using Deutchezz.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace Deutchezz.Infrastructure.Services;

public class EmailSender(ILogger<EmailSender> logger) : IEmailSender
{
    public Task SendAsync(string to, string subject, string body, CancellationToken cancellationToken)
    {
        logger.LogInformation("Email sent to {To} with subject {Subject}", to, subject);
        return Task.CompletedTask;
    }
}