using System.Diagnostics;
using Azure.Monitor.OpenTelemetry.Exporter;
using OpenTelemetry.Logs;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

static class HostedTelemetry
{
    internal static readonly ActivitySource Source = new("Support.Capstone");

    internal static void AddAzureMonitor(WebApplicationBuilder builder, Action<AzureMonitorExporterOptions> configure)
    {
        builder.Services.AddOpenTelemetry().ConfigureResource(resource => resource.AddService("support-capstone"))
            .WithTracing(tracing => ConfigureTracing(tracing.AddAzureMonitorTraceExporter(configure)));
        builder.Logging.AddFilter<OpenTelemetryLoggerProvider>((category, level) =>
            category == Source.Name && level >= LogLevel.Information);
        builder.Logging.AddOpenTelemetry(options => options
            .SetResourceBuilder(ResourceBuilder.CreateDefault().AddService("support-capstone"))
            .AddAzureMonitorLogExporter(configure));
    }

    // ASP.NET can supply a non-recording parent when only our bounded source is registered.
    // Apply this after the Azure Monitor exporter registers its own sampler.
    internal static TracerProviderBuilder ConfigureTracing(TracerProviderBuilder tracing) =>
        tracing.AddSource(Source.Name).SetSampler(new AlwaysOnSampler());

    internal static async Task<T> DependencyAsync<T>(string name, Func<Task<T>> request)
    {
        using var span = Source.StartActivity(name, ActivityKind.Client);
        try { return await request(); }
        catch
        {
            span?.SetStatus(ActivityStatusCode.Error);
            throw;
        }
    }
}
