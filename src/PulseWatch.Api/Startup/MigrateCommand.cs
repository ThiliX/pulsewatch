using PulseWatch.Infrastructure.Persistence;

namespace PulseWatch.Api.Startup;

/// <summary>
/// Development helper: <c>dotnet run --project src/PulseWatch.Api -- --migrate</c> applies migrations and exits.
/// Other environments must run migrations as a separate deployment step (EF Core migration bundle).
/// </summary>
internal static partial class MigrateCommand
{
    public const string Switch = "--migrate";

    public static bool IsRequested(string[] args) => args.Contains(Switch, StringComparer.Ordinal);

    public static async Task<int> RunAsync(WebApplication app)
    {
        if (!app.Environment.IsDevelopment())
        {
            LogRefused(app.Logger, Switch);
            return 1;
        }

        await PersistenceSetup.MigrateDatabaseAsync(app.Services).ConfigureAwait(false);
        LogApplied(app.Logger);
        return 0;
    }

    [LoggerMessage(EventId = 100, Level = LogLevel.Error,
        Message = "{Switch} is only allowed in Development. Use an EF Core migration bundle instead (see README).")]
    private static partial void LogRefused(ILogger logger, string @switch);

    [LoggerMessage(EventId = 101, Level = LogLevel.Information, Message = "Database migrations applied.")]
    private static partial void LogApplied(ILogger logger);
}
