using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using PulseWatch.Application.Security;
using PulseWatch.Domain.Monitoring;
using PulseWatch.Infrastructure.Identity;
using PulseWatch.Infrastructure.Persistence;
using PulseWatch.Infrastructure.Telemetry;
using PulseWatch.Worker;
using Testcontainers.PostgreSql;

// Usage: dotnet run --project tests/PulseWatch.LoadTest -c Release -- [monitors=500] [minutes=5]
// Runs one real worker (default settings) against a local HTTP server that answers in 20-80 ms, with
// every monitor due at the start (the worst case, e.g. right after a deploy), and prints a Markdown report.
var monitorCount = args.Length > 0 ? int.Parse(args[0], CultureInfo.InvariantCulture) : 500;
var minutes = args.Length > 1 ? double.Parse(args[1], CultureInfo.InvariantCulture) : 5;
var invariant = CultureInfo.InvariantCulture;

Console.WriteLine($"Starting PostgreSQL (Testcontainers) for {monitorCount} monitors over {minutes} min...");
await using var postgres = new PostgreSqlBuilder("postgres:18.6-alpine").Build();
await postgres.StartAsync();
var connectionString = new NpgsqlConnectionStringBuilder(postgres.GetConnectionString()) { MaxPoolSize = 50 }.ConnectionString;

await using var target = new LocalTarget();

var options = new DbContextOptionsBuilder<PulseWatchDbContext>();
PersistenceSetup.Configure(options, connectionString);
await using (var db = new PulseWatchDbContext(options.Options))
{
    await db.Database.MigrateAsync();
    var owner = new ApplicationUser { Id = "load-owner", UserName = "load@example.com", Email = "load@example.com", CreatedAtUtc = DateTimeOffset.UtcNow };
    db.Users.Add(owner);
    var now = DateTimeOffset.UtcNow;
    for (var i = 0; i < monitorCount; i++)
    {
        db.Monitors.Add(EndpointMonitor.Create(
            owner.Id, $"Load {i}", $"http://target{i}.test:{target.Port}/health", MonitorSettings.Default, now));
    }

    await db.SaveChangesAsync();
}

var dbSizeBefore = await DatabaseSizeAsync(connectionString);

var builder = Host.CreateApplicationBuilder();
builder.Logging.ClearProviders();
builder.Logging.AddConsole().SetMinimumLevel(LogLevel.Warning);
builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
{
    ["ConnectionStrings:PulseWatch"] = connectionString,
    ["Monitoring:AllowedPorts:0"] = target.Port.ToString(invariant),
    ["Worker:WorkerId"] = "load-test",
});
builder.Services.AddPulseWatchWorker(builder.Configuration);

// Load-test only: let probes reach the local server. The product has no switch for this.
builder.Services.RemoveAll<IDnsResolver>();
builder.Services.AddSingleton<IDnsResolver, LoopbackDns>();
builder.Services.RemoveAll<IAddressPolicy>();
builder.Services.AddSingleton<IAddressPolicy, LoopbackAllowedPolicy>();

using var host = builder.Build();
var started = Stopwatch.StartNew();
var lag = new ConcurrentQueue<(double At, double Seconds)>();
var durations = new ConcurrentQueue<double>();
long completed = 0, failed = 0;

using var listener = new MeterListener();
var meter = host.Services.GetRequiredService<PulseWatchMetrics>().Meter;
listener.InstrumentPublished = (instrument, l) =>
{
    if (ReferenceEquals(instrument.Meter, meter))
    {
        l.EnableMeasurementEvents(instrument);
    }
};
listener.SetMeasurementEventCallback<double>((instrument, value, _, _) =>
{
    if (instrument.Name == "pulsewatch.worker.lag")
    {
        lag.Enqueue((started.Elapsed.TotalSeconds, value));
    }
    else if (instrument.Name == "pulsewatch.check.duration")
    {
        durations.Enqueue(value);
    }
});
listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
{
    if (instrument.Name != "pulsewatch.checks.completed")
    {
        return;
    }

    Interlocked.Add(ref completed, value);
    foreach (var tag in tags)
    {
        if (tag.Key == "outcome" && !Equals(tag.Value, "Success"))
        {
            Interlocked.Add(ref failed, value);
        }
    }
});
listener.Start();

var process = Process.GetCurrentProcess();
var cpuBefore = process.TotalProcessorTime;
var peakWorkingSet = 0L;
await host.StartAsync();

var end = TimeSpan.FromMinutes(minutes);
while (started.Elapsed < end)
{
    await Task.Delay(TimeSpan.FromSeconds(5));
    process.Refresh();
    peakWorkingSet = Math.Max(peakWorkingSet, process.WorkingSet64);
    Console.WriteLine($"  {started.Elapsed:mm\\:ss}  checks {Interlocked.Read(ref completed),6}  last lag {(lag.IsEmpty ? 0 : lag.Last().Seconds):F2} s");
}

await host.StopAsync();
process.Refresh();
var wall = started.Elapsed;
var cpu = process.TotalProcessorTime - cpuBefore;
var dbSizeAfter = await DatabaseSizeAsync(connectionString);
long checkRows;
double avgGap, maxGap;
await using (var connection = new NpgsqlConnection(connectionString))
{
    await connection.OpenAsync();
    await using var count = new NpgsqlCommand("SELECT count(*) FROM monitor_checks", connection);
    checkRows = (long)(await count.ExecuteScalarAsync())!;

    // The real interval: time between consecutive checks of the same monitor (configured: 60 s).
    await using var gaps = new NpgsqlCommand(
        """
        SELECT extract(epoch FROM avg(gap))::float8, extract(epoch FROM max(gap))::float8
        FROM (SELECT started_at_utc - lag(started_at_utc) OVER (PARTITION BY monitor_id ORDER BY started_at_utc) AS gap
              FROM monitor_checks) t
        WHERE gap IS NOT NULL
        """, connection);
    await using var reader = await gaps.ExecuteReaderAsync();
    await reader.ReadAsync();
    avgGap = reader.IsDBNull(0) ? 0 : reader.GetDouble(0);
    maxGap = reader.IsDBNull(1) ? 0 : reader.GetDouble(1);
}

static double Percentile(IReadOnlyList<double> sorted, double p) =>
    sorted.Count == 0 ? 0 : sorted[Math.Min(sorted.Count - 1, (int)Math.Ceiling(p * sorted.Count) - 1)];

var report = new StringBuilder();
report.AppendLine(invariant, $"| Setting | Value |");
report.AppendLine("|---|---|");
report.AppendLine(invariant, $"| Monitors | {monitorCount} at 60 s interval, all due at the start |");
report.AppendLine(invariant, $"| Duration | {wall.TotalMinutes:F1} min |");
report.AppendLine(invariant, $"| Machine | {Environment.ProcessorCount} logical CPUs, {Environment.OSVersion.VersionString}, .NET {Environment.Version} |");
report.AppendLine(invariant, $"| Checks recorded | {completed} ({completed / wall.TotalMinutes:F0}/min, expected ≈ {monitorCount}/min), {failed} failed |");
report.AppendLine(invariant, $"| Time between checks of one monitor | average {avgGap:F2} s, max {maxGap:F2} s (configured 60 s) |");
report.AppendLine(invariant, $"| Worker CPU | {cpu.TotalSeconds / wall.TotalSeconds * 100:F1} % of one core on average ({cpu.TotalSeconds / wall.TotalSeconds * 100 / Environment.ProcessorCount:F1} % of the machine) |");
report.AppendLine(invariant, $"| Worker peak working set | {peakWorkingSet / 1024.0 / 1024.0:F0} MB |");
report.AppendLine(invariant, $"| Database growth | {(dbSizeAfter - dbSizeBefore) / 1024.0 / 1024.0:F1} MB for {checkRows} checks ≈ {(dbSizeAfter - dbSizeBefore) / (double)Math.Max(1, checkRows):F0} bytes per check incl. indexes |");
report.AppendLine(invariant, $"| Projected 30-day size | ≈ {(dbSizeAfter - dbSizeBefore) / (double)Math.Max(1, checkRows) * monitorCount * 60 * 24 * 30 / 1024 / 1024 / 1024:F1} GB of checks (retention keeps it there) |");
report.AppendLine();
report.AppendLine("Lag = how long after its due time a check was claimed, per minute of the run:");
report.AppendLine();
report.AppendLine("| Minute | Checks claimed | p50 lag | p95 lag | Max lag |");
report.AppendLine("|---|---|---|---|---|");
foreach (var group in lag.GroupBy(l => (int)(l.At / 60)).OrderBy(g => g.Key))
{
    var sorted = group.Select(g => g.Seconds).Order().ToList();
    report.AppendLine(invariant,
        $"| {group.Key + 1} | {sorted.Count} | {Percentile(sorted, 0.5):F2} s | {Percentile(sorted, 0.95):F2} s | {sorted[^1]:F2} s |");
}

var allDurations = durations.Order().ToList();
report.AppendLine();
report.AppendLine(invariant, $"Probe duration (local target answering in 20-80 ms): p50 {Percentile(allDurations, 0.5):F0} ms, p95 {Percentile(allDurations, 0.95):F0} ms.");

Console.WriteLine();
Console.WriteLine(report.ToString());
return 0;

static async Task<long> DatabaseSizeAsync(string connectionString)
{
    await using var connection = new NpgsqlConnection(connectionString);
    await connection.OpenAsync();
    await using var command = new NpgsqlCommand("SELECT pg_database_size(current_database())", connection);
    return (long)(await command.ExecuteScalarAsync())!;
}

/// <summary>A small HTTP/1.1 server on 127.0.0.1 that answers 200 after 20-80 ms, like a typical health endpoint.</summary>
internal sealed class LocalTarget : IAsyncDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _loop;

    public LocalTarget()
    {
        _listener.Start(backlog: 1024);
        _loop = Task.Run(AcceptLoopAsync);
    }

    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    private async Task AcceptLoopAsync()
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                var client = await _listener.AcceptTcpClientAsync(_stop.Token);
                _ = Task.Run(() => HandleAsync(client));
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or SocketException)
        {
        }
    }

    private async Task HandleAsync(TcpClient client)
    {
        using (client)
        {
            try
            {
                var stream = client.GetStream();
                var buffer = new byte[4096];
                var received = new StringBuilder();
                while (!received.ToString().Contains("\r\n\r\n", StringComparison.Ordinal))
                {
                    var read = await stream.ReadAsync(buffer, _stop.Token);
                    if (read == 0)
                    {
                        return;
                    }

                    received.Append(Encoding.ASCII.GetString(buffer, 0, read));
                }

                await Task.Delay(Random.Shared.Next(20, 80), _stop.Token);
                await stream.WriteAsync("HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\nok"u8.ToArray(), _stop.Token);
            }
            catch (Exception ex) when (ex is IOException or OperationCanceledException or SocketException)
            {
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        _listener.Stop();
        try
        {
            await _loop;
        }
        catch (OperationCanceledException)
        {
        }

        _stop.Dispose();
    }
}

/// <summary>Allows loopback in addition to public addresses. Load-test only.</summary>
internal sealed class LoopbackAllowedPolicy : IAddressPolicy
{
    public bool IsAllowed(IPAddress address) => IPAddress.IsLoopback(address) || PublicAddressPolicy.IsPublic(address);
}

/// <summary>Resolves every host name to 127.0.0.1. Load-test only.</summary>
internal sealed class LoopbackDns : IDnsResolver
{
    public Task<IReadOnlyList<IPAddress>> ResolveAsync(string host, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<IPAddress>>([IPAddress.Loopback]);
}
