using PulseWatch.Worker;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddSingleton(TimeProvider.System);
builder.Services
    .AddOptions<HeartbeatOptions>()
    .Bind(builder.Configuration.GetSection(HeartbeatOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddHostedService<HeartbeatService>();

var host = builder.Build();
host.Run();
