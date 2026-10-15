using Deutchezz.Application.Abstractions;
using Deutchezz.Application.Common.Options;
using Deutchezz.Application.Contact;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Deutchezz.UnitTests.Contact;

public class ContactServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 1, 9, 30, 0, TimeSpan.Zero);

    private static readonly ContactRequest Valid = new(
        "Ada Obi", "ada@example.com", "Hello, I would like to ask about room availability for December.");

    private static ContactService CreateService(
        CapturingEmailSender sender, string adminAddress = "front-desk@example.com", string fromAddress = "noreply@example.com")
    {
        var options = Options.Create(new EmailOptions
        {
            AdminNotificationAddress = adminAddress,
            Smtp = new EmailOptions.SmtpOptions { FromAddress = fromAddress }
        });
        return new ContactService(sender, options, new FixedTimeProvider(Now), NullLogger<ContactService>.Instance);
    }

    [Fact]
    public async Task Valid_message_is_emailed_to_the_admin_inbox_with_reply_to_the_visitor()
    {
        var sender = new CapturingEmailSender();
        var service = CreateService(sender);

        var result = await service.SendAsync(Valid, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var sent = Assert.Single(sender.Sent);
        Assert.Equal("front-desk@example.com", sent.To);
        Assert.Equal("ada@example.com", sent.ReplyTo);
        Assert.Contains("Contact form", sent.Subject);
        Assert.Contains(Valid.Message, sent.TextBody);
        Assert.Contains("Ada Obi", sent.HtmlBody);
    }

    [Fact]
    public async Task Falls_back_to_the_from_address_when_no_admin_inbox_is_configured()
    {
        var sender = new CapturingEmailSender();
        var service = CreateService(sender, adminAddress: "");

        var result = await service.SendAsync(Valid, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("noreply@example.com", Assert.Single(sender.Sent).To);
    }

    [Fact]
    public async Task Drops_the_message_but_still_acknowledges_when_no_recipient_is_configured()
    {
        var sender = new CapturingEmailSender();
        var service = CreateService(sender, adminAddress: "", fromAddress: "");

        var result = await service.SendAsync(Valid, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(sender.Sent);
    }

    [Theory]
    [InlineData("", "ada@example.com", "This is a long enough message.")]
    [InlineData("A", "ada@example.com", "This is a long enough message.")]
    [InlineData("Ada Obi", "not-an-email", "This is a long enough message.")]
    [InlineData("Ada Obi", "", "This is a long enough message.")]
    [InlineData("Ada Obi", "ada@example.com", "too short")]
    public async Task Invalid_submissions_are_rejected_without_sending(string name, string email, string message)
    {
        var sender = new CapturingEmailSender();
        var service = CreateService(sender);

        var result = await service.SendAsync(new ContactRequest(name, email, message), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(Deutchezz.Domain.Common.ErrorType.Validation, result.Error.Type);
        Assert.Empty(sender.Sent);
    }

    private sealed class CapturingEmailSender : IEmailSender
    {
        public List<SentEmail> Sent { get; } = new();

        public Task SendAsync(string to, string subject, string htmlBody, string? textBody = null,
            CancellationToken cancellationToken = default, string? replyTo = null)
        {
            Sent.Add(new SentEmail(to, subject, htmlBody, textBody, replyTo));
            return Task.CompletedTask;
        }

        public sealed record SentEmail(string To, string Subject, string HtmlBody, string? TextBody, string? ReplyTo);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
