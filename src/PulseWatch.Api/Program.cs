using System.Diagnostics;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using PulseWatch.Api.Auth;
using PulseWatch.Api.Errors;
using PulseWatch.Api.Health;
using PulseWatch.Api.Incidents;
using PulseWatch.Api.Monitors;
using PulseWatch.Api.OpenApi;
using PulseWatch.Api.RateLimiting;
using PulseWatch.Api.Startup;
using PulseWatch.Application.Monitors;
using PulseWatch.Application.Options;
using PulseWatch.Application.Security;
using PulseWatch.Infrastructure.Network;
using PulseWatch.Infrastructure.Persistence;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddOptions<MonitoringOptions>()
    .Bind(builder.Configuration.GetSection(MonitoringOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddPulseWatchPersistence(builder.Configuration);
builder.Services.AddPulseWatchNetwork();
builder.Services.AddSingleton<IDestinationValidator>(sp => new DestinationValidator(
    sp.GetRequiredService<IDnsResolver>(),
    sp.GetRequiredService<IOptions<MonitoringOptions>>().Value.AllowedPorts));
builder.Services.AddSingleton(sp => new MonitorRequestValidator(
    sp.GetRequiredService<IDestinationValidator>(),
    sp.GetRequiredService<IOptions<MonitoringOptions>>().Value));

builder.Services.AddPulseWatchAuth(builder.Environment);
builder.Services.AddPulseWatchRateLimiting(builder.Configuration);

builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
    context.ProblemDetails.Extensions["traceId"] = Activity.Current?.Id ?? context.HttpContext.TraceIdentifier);
builder.Services.AddExceptionHandler<KnownExceptionHandler>();
builder.Services.AddPulseWatchOpenApi();
builder.Services.AddHealthChecks().AddPulseWatchDatabaseCheck(HealthRoutes.ReadyTag);

var app = builder.Build();

if (MigrateCommand.IsRequested(args))
{
    return await MigrateCommand.RunAsync(app);
}

app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference(options => options.WithTitle("PulseWatch API"));
}
else
{
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

// Liveness: the process is up. Runs no checks, so it never fails because a dependency is down.
app.MapHealthChecks(HealthRoutes.Live, new HealthCheckOptions { Predicate = _ => false });

// Readiness: database reachable and migrated. Responds with Healthy/Unhealthy text only, never details.
app.MapHealthChecks(HealthRoutes.Ready, new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains(HealthRoutes.ReadyTag),
});

app.MapAuthEndpoints();
app.MapMonitorEndpoints();
app.MapMonitorHistoryEndpoints();
app.MapIncidentEndpoints();

await app.RunAsync();
return 0;
