using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using PulseWatch.Api.Health;
using PulseWatch.Api.Startup;
using PulseWatch.Application.Options;
using PulseWatch.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddOptions<MonitoringOptions>()
    .Bind(builder.Configuration.GetSection(MonitoringOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddPulseWatchPersistence(builder.Configuration);

builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks().AddPulseWatchDatabaseCheck(HealthRoutes.ReadyTag);

var app = builder.Build();

if (MigrateCommand.IsRequested(args))
{
    return await MigrateCommand.RunAsync(app);
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}
else
{
    app.UseExceptionHandler();
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseStatusCodePages();

// Liveness: the process is up. Runs no checks, so it never fails because a dependency is down.
app.MapHealthChecks(HealthRoutes.Live, new HealthCheckOptions { Predicate = _ => false });

// Readiness: database reachable and migrated. Responds with Healthy/Unhealthy text only, never details.
app.MapHealthChecks(HealthRoutes.Ready, new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains(HealthRoutes.ReadyTag),
});

await app.RunAsync();
return 0;
