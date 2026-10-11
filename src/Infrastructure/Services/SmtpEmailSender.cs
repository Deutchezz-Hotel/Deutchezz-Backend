using Deutchezz.Application.Abstractions;
using Deutchezz.Application.Common.Options;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;

namespace Deutchezz.Infrastructure.Services;

public class SmtpEmailSender(IOptions<EmailOptions> options, ILogger<SmtpEmailSender> logger) : IEmailSender
{
    public async Task SendAsync(string to, string subject, string htmlBody, string? textBody = null, CancellationToken cancellationToken = default)
    {
        var smtp = options.Value.Smtp;

        try
        {
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(smtp.FromName, smtp.FromAddress));
            message.To.Add(MailboxAddress.Parse(to));
            message.Subject = subject;

            var builder = new BodyBuilder
            {
                HtmlBody = htmlBody,
                TextBody = textBody ?? StripTags(htmlBody)
            };
            message.Body = builder.ToMessageBody();

            using var client = new SmtpClient();
            await client.ConnectAsync(smtp.Host, smtp.Port, SecureSocketOptions.StartTls, cancellationToken);
            if (smtp.HasCredentials)
                await client.AuthenticateAsync(smtp.Username, smtp.Password, cancellationToken);
            await client.SendAsync(message, cancellationToken);
            await client.DisconnectAsync(true, cancellationToken);

            logger.LogInformation("Email sent to {To} subject {Subject}", to, subject);
        }
        catch (Exception ex)
        {
            // Delivery must never fail the calling business operation (see IEmailSender contract).
            logger.LogError(ex, "Failed to send email to {To} subject {Subject}", to, subject);
        }
    }

    private static string StripTags(string html) =>
        System.Text.RegularExpressions.Regex.Replace(html, "<[^>]+>", " ").Trim();
}