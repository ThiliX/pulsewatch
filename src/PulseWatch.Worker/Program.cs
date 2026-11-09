namespace PulseWatch.Worker;

// An explicit entry point (instead of top-level statements) keeps this class out of the global
// namespace, where it would clash with the API's Program in the integration tests.
internal static class WorkerProgram
{
    private static void Main(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);
        builder.Services.AddPulseWatchWorker(builder.Configuration);
        builder.Build().Run();
    }
}
