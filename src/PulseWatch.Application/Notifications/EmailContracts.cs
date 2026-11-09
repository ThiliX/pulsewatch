namespace PulseWatch.Application.Notifications;

/// <summary>A ready-to-send email. Built only from stored data by <see cref="NotificationEmailBuilder"/>.</summary>
public sealed record EmailMessage(string To, string Subject, string TextBody, string HtmlBody);

/// <summary>Sends one email. Throws <see cref="EmailDeliveryException"/> when delivery fails.</summary>
public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken);
}

/// <summary>
/// Fixed labels stored in notification_outbox.last_error_category and shown in the UI. Exception
/// messages are never stored, because they can contain server replies, host names or addresses.
/// </summary>
public static class EmailErrorCategories
{
    public const string ConnectionFailed = "ConnectionFailed";
    public const string Timeout = "Timeout";
    public const string TlsFailed = "TlsFailed";
    public const string AuthenticationFailed = "AuthenticationFailed";
    public const string SenderRejected = "SenderRejected";
    public const string RecipientRejected = "RecipientRejected";
    public const string MessageRejected = "MessageRejected";
    public const string ServerRejected = "ServerRejected";
    public const string InvalidRecipient = "InvalidRecipient";
    public const string Interrupted = "Interrupted";
    public const string Unknown = "Unknown";
}

/// <summary>A failed send. <see cref="Permanent"/> means retrying cannot help (for example, the address doesn't exist).</summary>
public sealed class EmailDeliveryException : Exception
{
    public EmailDeliveryException(string category, bool permanent, Exception? innerException = null)
        : base($"Email delivery failed: {category}.", innerException)
    {
        Category = category;
        Permanent = permanent;
    }

    public EmailDeliveryException()
        : this(EmailErrorCategories.Unknown, permanent: false)
    {
    }

    // The standard constructors classify as Unknown and transient (retried).
    public EmailDeliveryException(string message)
        : base(message)
    {
        Category = EmailErrorCategories.Unknown;
    }

    public EmailDeliveryException(string message, Exception innerException)
        : base(message, innerException)
    {
        Category = EmailErrorCategories.Unknown;
    }

    public string Category { get; }

    public bool Permanent { get; }
}
