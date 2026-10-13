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
    private const int MaxAttempts = 3;

    public async Task SendAsync(string to, string subject, string htmlBody, string? textBody = null, CancellationToken cancellationToken = default)
    {
        var smtp = options.Value.Smtp;

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
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

                // A fresh client per attempt: a failed connect can leave one unusable, and DNS
                // blips ("No such host is known") are routinely transient.
                using var client = new SmtpClient();
                await client.ConnectAsync(smtp.Host, smtp.Port, SecureSocketOptions.StartTls, cancellationToken);
                if (smtp.HasCredentials)
                    await client.AuthenticateAsync(smtp.Username, smtp.Password, cancellationToken);
                await client.SendAsync(message, cancellationToken);
                await client.DisconnectAsync(true, cancellationToken);

                logger.LogInformation("Email sent to {To} subject {Subject}", to, subject);
                return;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning("Email to {To} subject {Subject} was cancelled", to, subject);
                return;
            }
            catch (Exception ex) when (attempt < MaxAttempts)
            {
                logger.LogWarning(ex,
                    "Attempt {Attempt}/{Max} to send email to {To} via {Host} failed; retrying",
                    attempt, MaxAttempts, to, smtp.Host);
                await Task.Delay(TimeSpan.FromMilliseconds(300 * attempt), cancellationToken);
            }
            catch (Exception ex)
            {
                // Delivery must never fail the calling business operation (see IEmailSender contract).
                logger.LogError(ex,
                    "Failed to send email to {To} subject {Subject} via {Host} after {Attempts} attempts",
                    to, subject, smtp.Host, MaxAttempts);
            }
        }
    }

    private static string StripTags(string html) =>
        System.Text.RegularExpressions.Regex.Replace(html, "<[^>]+>", " ").Trim();
}