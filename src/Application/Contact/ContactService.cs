using System.Net.Mail;
using Deutchezz.Application.Abstractions;
using Deutchezz.Application.Common.Options;
using Deutchezz.Application.Emails;
using Deutchezz.Domain.Common;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Deutchezz.Application.Contact;

public record ContactRequest(string Name, string Email, string Message);

public record ContactResponse(string Message);

public interface IContactService
{
    Task<Result<ContactResponse>> SendAsync(ContactRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// Handles the public contact form: validates the submission and emails it to the hotel's
/// notification inbox with the visitor set as Reply-To. The visitor's IP/identity is unknown
/// (the endpoint is anonymous), so the message is treated as untrusted text.
/// </summary>
public class ContactService(
    IEmailSender emailSender,
    IOptions<EmailOptions> emailOptions,
    TimeProvider timeProvider,
    ILogger<ContactService> logger) : IContactService
{
    private const int NameMin = 2;
    private const int NameMax = 120;
    private const int EmailMax = 254;
    private const int MessageMin = 10;
    private const int MessageMax = 4000;

    public async Task<Result<ContactResponse>> SendAsync(ContactRequest request, CancellationToken cancellationToken)
    {
        var name = request.Name?.Trim() ?? string.Empty;
        var email = request.Email?.Trim() ?? string.Empty;
        var message = request.Message?.Trim() ?? string.Empty;

        if (name.Length is < NameMin or > NameMax)
            return Result<ContactResponse>.Failure(Error.Validation("contact.name_invalid",
                $"Name must be between {NameMin} and {NameMax} characters."));

        if (email.Length is 0 or > EmailMax || !IsValidEmail(email))
            return Result<ContactResponse>.Failure(Error.Validation("contact.email_invalid",
                "A valid email address is required."));

        if (message.Length is < MessageMin or > MessageMax)
            return Result<ContactResponse>.Failure(Error.Validation("contact.message_invalid",
                $"Message must be between {MessageMin} and {MessageMax} characters."));

        var options = emailOptions.Value;
        var recipient = string.IsNullOrWhiteSpace(options.AdminNotificationAddress)
            ? options.Smtp.FromAddress
            : options.AdminNotificationAddress;

        if (string.IsNullOrWhiteSpace(recipient))
        {
            // Nothing to deliver to (not configured). Log for operators; still acknowledge the
            // visitor so the public endpoint never leaks infrastructure state.
            logger.LogWarning("Contact message received but no notification recipient is configured; message dropped");
            return Result<ContactResponse>.Success(new ContactResponse("Thanks for reaching out — we'll be in touch soon."));
        }

        var model = new ContactMessageModel(name, email, message, timeProvider.GetUtcNow());
        var (subject, html, text) = ContactMessageEmail.Build(model);

        // The sender swallows delivery failures (see IEmailSender contract); we still await it so
        // the request is fast and the visitor gets an immediate acknowledgment.
        await emailSender.SendAsync(recipient, subject, html, text, cancellationToken, replyTo: email);

        return Result<ContactResponse>.Success(new ContactResponse("Thanks for reaching out — we'll be in touch soon."));
    }

    private static bool IsValidEmail(string value)
    {
        try
        {
            var address = new MailAddress(value);
            return string.Equals(address.Address, value, StringComparison.OrdinalIgnoreCase);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
