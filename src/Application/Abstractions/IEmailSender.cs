namespace Deutchezz.Application.Abstractions;

public interface IEmailSender
{
    /// <summary>
    /// Sends an email. <paramref name="htmlBody"/> is the HTML version; <paramref name="textBody"/>
    /// is the optional plain-text fallback; <paramref name="replyTo"/>, when supplied, is set as
    /// the message's Reply-To address (e.g. the visitor who submitted a contact form) so the
    /// recipient can reply directly. Implementations must never throw for delivery failures in a
    /// way that fails the calling business operation — the caller catches.
    /// </summary>
    Task SendAsync(string to, string subject, string htmlBody, string? textBody = null, CancellationToken cancellationToken = default, string? replyTo = null);
}