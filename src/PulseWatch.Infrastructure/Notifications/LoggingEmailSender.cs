using Microsoft.Extensions.Logging;
using PulseWatch.Application.Notifications;

namespace PulseWatch.Infrastructure.Notifications;

/// <summary>
/// Development default: logs that an email would have been sent, instead of sending it. Only the recipient
/// and subject are logged; the body contains the monitored URL's path, which must not reach logs. To read
/// full emails locally, use the Smtp provider with Mailpit (see README).
/// </summary>
internal sealed partial class LoggingEmailSender(ILogger<LoggingEmailSender> logger) : IEmailSender
{
    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        LogEmail(logger, message.To, message.Subject);
        return Task.CompletedTask;
    }

    [LoggerMessage(EventId = 3100, Level = LogLevel.Information,
        Message = "Email not sent (Notifications:Provider is Logging). To: {To}; Subject: {Subject}")]
    private static partial void LogEmail(ILogger logger, string to, string subject);
}
