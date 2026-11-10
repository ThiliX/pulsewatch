using PulseWatch.Infrastructure.Persistence;

namespace PulseWatch.Api.Startup;

/// <summary>
/// Development helper: <c>dotnet run --project src/PulseWatch.Api -- seed-demo</c> applies migrations,
/// creates the demo account with a week of history, prints the sign-in details and exits.
/// Refused outside Development, because it creates an account with a known address.
/// </summary>
internal static partial class SeedDemoCommand
{
    public const string Verb = "seed-demo";

    public static bool IsRequested(string[] args) => args.Contains(Verb, StringComparer.Ordinal);

    public static async Task<int> RunAsync(WebApplication app, TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(output);
        if (!app.Environment.IsDevelopment())
        {
            LogRefused(app.Logger, Verb, app.Environment.EnvironmentName);
            return 1;
        }

        await PersistenceSetup.MigrateDatabaseAsync(app.Services).ConfigureAwait(false);

        var scope = app.Services.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var seeder = ActivatorUtilities.CreateInstance<DemoSeeder>(scope.ServiceProvider);
            var result = await seeder.SeedAsync(CancellationToken.None).ConfigureAwait(false);

            // Written to the console, not the log: it's a one-time local sign-in for a demo account.
            await output.WriteLineAsync($"""

                Demo data ready: {result.Monitors} monitors, {result.Checks} checks, {result.Incidents} incidents (7 days).
                Sign in at http://localhost:5173 with
                  Email:    {result.Email}
                  Password: {result.Password}
                Running seed-demo again replaces the demo monitors and sets a new password.
                Start the worker to keep the monitors checking.

                """).ConfigureAwait(false);
        }

        return 0;
    }

    [LoggerMessage(EventId = 102, Level = LogLevel.Error,
        Message = "{Verb} is only allowed in Development (current environment: {Environment}).")]
    private static partial void LogRefused(ILogger logger, string verb, string environment);
}
