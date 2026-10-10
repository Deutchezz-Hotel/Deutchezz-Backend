namespace Deutchezz.Application.Abstractions;

public interface IEmailSender
{
    /// <summary>
    /// Sends an email. <paramref name="htmlBody"/> is the HTML version; <paramref name="textBody"/>
    /// is the optional plain-text fallback. Implementations must never throw for delivery
    /// failures in a way that fails the calling business operation — the caller catches.
    /// </summary>
    Task SendAsync(string to, string subject, string htmlBody, string? textBody = null, CancellationToken cancellationToken = default);
}