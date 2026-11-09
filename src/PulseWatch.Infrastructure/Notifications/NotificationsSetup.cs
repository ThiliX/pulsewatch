using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PulseWatch.Application.Notifications;
using PulseWatch.Application.Options;

namespace PulseWatch.Infrastructure.Notifications;

public static class NotificationsSetup
{
    /// <summary>
    /// Registers the outbox store and the email sender chosen by <paramref name="provider"/>.
    /// Options binding and validation stay in the host (see WorkerSetup). TryAdd lets tests swap the sender.
    /// </summary>
    public static IServiceCollection AddPulseWatchNotifications(this IServiceCollection services, EmailProvider provider)
    {
        services.AddScoped<INotificationOutboxStore, OutboxStore>();
        if (provider == EmailProvider.Smtp)
        {
            services.TryAddSingleton<IEmailSender, SmtpEmailSender>();
        }
        else
        {
            services.TryAddSingleton<IEmailSender, LoggingEmailSender>();
        }

        return services;
    }
}
