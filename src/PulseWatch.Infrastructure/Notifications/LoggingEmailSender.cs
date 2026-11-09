using Microsoft.Extensions.Logging;
using PulseWatch.Application.Notifications;

namespace PulseWatch.Infrastructure.Notifications;

/// <summary>Development default: writes the email to the log instead of sending it.</summary>
internal sealed partial class LoggingEmailSender(ILogger<LoggingEmailSender> logger) : IEmailSender
{
    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        LogEmail(logger, message.To, message.Subject, message.TextBody);
        return Task.CompletedTask;
    }

    [LoggerMessage(EventId = 3100, Level = LogLevel.Information,
        Message = "Email not sent (Notifications:Provider is Logging). To: {To}; Subject: {Subject}\n{Body}")]
    private static partial void LogEmail(ILogger logger, string to, string subject, string body);
}
