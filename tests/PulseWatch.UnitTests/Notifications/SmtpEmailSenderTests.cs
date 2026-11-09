using System.Net.Sockets;
using MailKit.Net.Smtp;
using MailKit.Security;
using PulseWatch.Application.Notifications;
using PulseWatch.Application.Options;
using PulseWatch.Infrastructure.Notifications;

namespace PulseWatch.UnitTests.Notifications;

public class SmtpEmailSenderTests
{
    public static TheoryData<Exception, string, bool> Errors => new()
    {
        { new SmtpCommandException(SmtpErrorCode.RecipientNotAccepted, SmtpStatusCode.MailboxUnavailable, "no such user"), EmailErrorCategories.RecipientRejected, true },
        { new SmtpCommandException(SmtpErrorCode.RecipientNotAccepted, SmtpStatusCode.MailboxBusy, "try later"), EmailErrorCategories.ServerRejected, false },
        { new SmtpCommandException(SmtpErrorCode.MessageNotAccepted, SmtpStatusCode.TransactionFailed, "spam"), EmailErrorCategories.MessageRejected, true },
        { new SmtpCommandException(SmtpErrorCode.SenderNotAccepted, SmtpStatusCode.MailboxUnavailable, "bad sender"), EmailErrorCategories.SenderRejected, false },
        { new SmtpCommandException(SmtpErrorCode.UnexpectedStatusCode, SmtpStatusCode.ServiceNotAvailable, "closing"), EmailErrorCategories.ServerRejected, false },
        { new AuthenticationException("535 bad credentials"), EmailErrorCategories.AuthenticationFailed, false },
        { new SocketException((int)SocketError.ConnectionRefused), EmailErrorCategories.ConnectionFailed, false },
        { new IOException("reset"), EmailErrorCategories.ConnectionFailed, false },
        { new SmtpProtocolException("garbage"), EmailErrorCategories.ConnectionFailed, false },
        { new TimeoutException(), EmailErrorCategories.Timeout, false },
        { new OperationCanceledException(), EmailErrorCategories.Timeout, false },
        { new InvalidOperationException("?"), EmailErrorCategories.Unknown, false },
    };

    [Theory]
    [MemberData(nameof(Errors))]
    public void Errors_map_to_fixed_categories_and_only_bad_addresses_are_permanent(
        Exception error, string expectedCategory, bool expectedPermanent)
    {
        var result = SmtpEmailSender.Classify(error);

        Assert.Equal(expectedCategory, result.Category);
        Assert.Equal(expectedPermanent, result.Permanent);
        Assert.Same(error, result.InnerException);
    }

    [Theory]
    [InlineData(587, true, SecureSocketOptions.StartTls)]
    [InlineData(25, true, SecureSocketOptions.StartTls)]
    [InlineData(1025, false, SecureSocketOptions.None)]
    [InlineData(465, false, SecureSocketOptions.SslOnConnect)]
    [InlineData(465, true, SecureSocketOptions.SslOnConnect)]
    public void Tls_mode_never_silently_downgrades(int port, bool useStartTls, SecureSocketOptions expected) =>
        Assert.Equal(expected, SmtpEmailSender.SocketOptions(new EmailOptions { Port = port, UseStartTls = useStartTls }));

    [Fact]
    public void The_mime_message_has_both_bodies_and_a_single_line_subject()
    {
        var mime = SmtpEmailSender.BuildMime(
            new EmailMessage("owner@example.com", "Down\r\nBcc: x@example.com", "text", "<p>html</p>"),
            new EmailOptions { FromAddress = "alerts@example.com", FromName = "Pulse\nWatch" });

        Assert.Equal("Down  Bcc: x@example.com", mime.Subject);
        Assert.Empty(mime.Bcc);
        Assert.Equal("owner@example.com", Assert.Single(mime.To.Mailboxes).Address);
        Assert.Equal("Pulse Watch", Assert.Single(mime.From.Mailboxes).Name);
        Assert.Equal("text", mime.TextBody);
        Assert.Equal("<p>html</p>", mime.HtmlBody);
    }

    [Fact]
    public async Task An_unparseable_recipient_is_a_permanent_failure()
    {
        var sender = new SmtpEmailSender(
            Microsoft.Extensions.Options.Options.Create(new EmailOptions { Host = "localhost", FromAddress = "a@example.com" }),
            Microsoft.Extensions.Options.Options.Create(new NotificationOptions()));

        var error = await Assert.ThrowsAsync<EmailDeliveryException>(() =>
            sender.SendAsync(new EmailMessage("not an address <", "s", "t", "h"), TestContext.Current.CancellationToken));

        Assert.Equal(EmailErrorCategories.InvalidRecipient, error.Category);
        Assert.True(error.Permanent);
    }
}
