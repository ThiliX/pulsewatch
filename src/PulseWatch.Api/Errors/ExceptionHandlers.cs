using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using PulseWatch.Domain.Common;

namespace PulseWatch.Api.Errors;

/// <summary>
/// Turns known exceptions into Problem Details. Anything else falls through to the default handler,
/// which returns a generic 500 without stack traces or internal messages.
/// </summary>
internal sealed partial class KnownExceptionHandler(IProblemDetailsService problemDetails, ILogger<KnownExceptionHandler> logger)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var (status, title, detail) = exception switch
        {
            // Domain messages are written by us and safe to show (e.g. "Interval must be between 60 and 3600.").
            DomainException ex => (StatusCodes.Status400BadRequest, "The request breaks a business rule.", ex.Message),
            // Malformed JSON, wrong content type, oversized body... (thrown in Development, logged elsewhere).
            BadHttpRequestException ex => (ex.StatusCode, "The request could not be read.",
                "Check that the body is valid JSON and matches the documented schema."),
            DbUpdateConcurrencyException => (StatusCodes.Status412PreconditionFailed,
                "The resource was changed by another request.", "Reload it and apply your change again."),
            _ => (0, null, null),
        };

        if (status == 0)
        {
            return false;
        }

        LogHandled(logger, status, title!);
        httpContext.Response.StatusCode = status;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = { Status = status, Title = title, Detail = detail },
        }).ConfigureAwait(false);
    }

    [LoggerMessage(EventId = 200, Level = LogLevel.Information, Message = "Request ended with HTTP {StatusCode}: {Title}")]
    private static partial void LogHandled(ILogger logger, int statusCode, string title);
}
