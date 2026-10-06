using Deutchezz.Domain.Common;

namespace Deutchezz.Application.Abstractions;

public interface IEmailSender
{
    Task SendAsync(string to, string subject, string body, CancellationToken cancellationToken);
}