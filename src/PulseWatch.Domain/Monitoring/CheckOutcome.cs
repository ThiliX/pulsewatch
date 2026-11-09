namespace PulseWatch.Domain.Monitoring;

/// <summary>Result category of one probe attempt. Stored as smallint; do not renumber.</summary>
public enum CheckOutcome : short
{
    Success = 0,
    HttpStatusFailure = 1,
    Timeout = 2,
    DnsFailure = 3,
    TlsFailure = 4,
    ConnectionFailure = 5,
    RedirectRejected = 6,
    DestinationBlocked = 7,
    ResponseTooLarge = 8,
    UnexpectedError = 9,
}
