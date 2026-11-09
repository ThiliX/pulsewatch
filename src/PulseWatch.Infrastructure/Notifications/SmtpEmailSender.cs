using System.Net.Sockets;
using MailKit;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;
using PulseWatch.Application.Notifications;
using PulseWatch.Application.Options;

namespace PulseWatch.Infrastructure.Notifications;

/// <summary>
/// Sends through SMTP with MailKit: one connection per email (alerts are rare, so pooling isn't worth
/// the complexity). Failures become <see cref="EmailDeliveryException"/> with a fixed category; the
/// server's reply text is never stored. No protocol logger is attached, so credentials can't reach logs.
/// </summary>
internal sealed class SmtpEmailSender(IOptions<EmailOptions> emailOptions, IOptions<NotificationOptions> notificationOptions)
    : IEmailSender
{
    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        var settings = emailOptions.Value;

        MimeMessage mime;
        try
        {
            mime = BuildMime(message, settings);
        }
        catch (ParseException ex)
        {
            throw new EmailDeliveryException(EmailErrorCategories.InvalidRecipient, permanent: true, ex);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var timeoutSeconds = notificationOptions.Value.SendTimeoutSeconds;
        timeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

        using var client = new SmtpClient { Timeout = timeoutSeconds * 1000 };
        try
        {
            await client.ConnectAsync(settings.Host ?? string.Empty, settings.Port, SocketOptions(settings), timeout.Token).ConfigureAwait(false);
            if (!string.IsNullOrEmpty(settings.Username))
            {
                await client.AuthenticateAsync(settings.Username, settings.Password ?? string.Empty, timeout.Token)
                    .ConfigureAwait(false);
            }

            await client.SendAsync(mime, timeout.Token).ConfigureAwait(false);
            await client.DisconnectAsync(quit: true, timeout.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not EmailDeliveryException && !cancellationToken.IsCancellationRequested)
        {
            throw Classify(ex);
        }
    }

    internal static MimeMessage BuildMime(EmailMessage message, EmailOptions settings)
    {
        var mime = new MimeMessage();
        mime.From.Add(new MailboxAddress(
            NotificationEmailBuilder.SingleLine(settings.FromName, 100), settings.FromAddress ?? string.Empty));
        mime.To.Add(MailboxAddress.Parse(message.To));
        mime.Subject = NotificationEmailBuilder.SingleLine(message.Subject, 200);
        mime.Body = new BodyBuilder { TextBody = message.TextBody, HtmlBody = message.HtmlBody }.ToMessageBody();
        return mime;
    }

    internal static SecureSocketOptions SocketOptions(EmailOptions settings) =>
        settings.Port == 465 ? SecureSocketOptions.SslOnConnect
        : settings.UseStartTls ? SecureSocketOptions.StartTls
        : SecureSocketOptions.None;

    /// <summary>Maps MailKit and network errors to a fixed category. Only bad-address errors are permanent.</summary>
    internal static EmailDeliveryException Classify(Exception exception) => exception switch
    {
        SmtpCommandException { ErrorCode: SmtpErrorCode.RecipientNotAccepted } smtp when IsPermanent(smtp) =>
            new(EmailErrorCategories.RecipientRejected, permanent: true, exception),
        SmtpCommandException { ErrorCode: SmtpErrorCode.MessageNotAccepted } smtp when IsPermanent(smtp) =>
            new(EmailErrorCategories.MessageRejected, permanent: true, exception),
        // A rejected sender is a configuration problem; retrying gives the operator time to fix it.
        SmtpCommandException { ErrorCode: SmtpErrorCode.SenderNotAccepted } =>
            new(EmailErrorCategories.SenderRejected, permanent: false, exception),
        SmtpCommandException => new(EmailErrorCategories.ServerRejected, permanent: false, exception),
        AuthenticationException => new(EmailErrorCategories.AuthenticationFailed, permanent: false, exception),
        SslHandshakeException => new(EmailErrorCategories.TlsFailed, permanent: false, exception),
        TimeoutException or OperationCanceledException => new(EmailErrorCategories.Timeout, permanent: false, exception),
        SocketException or IOException or SmtpProtocolException or ServiceNotConnectedException =>
            new(EmailErrorCategories.ConnectionFailed, permanent: false, exception),
        _ => new(EmailErrorCategories.Unknown, permanent: false, exception),
    };

    private static bool IsPermanent(SmtpCommandException exception) => (int)exception.StatusCode >= 500;
}
