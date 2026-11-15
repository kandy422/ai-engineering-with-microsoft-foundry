using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Text.Json;
using Azure.Core.Pipeline;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Trace;

static class TelemetryWireCheck
{
    internal static async Task RunAsync(Action<bool,string> check)
    {
        const string connection = "InstrumentationKey=00000000-0000-0000-0000-000000000001;IngestionEndpoint=https://127.0.0.1/";
        const string privateMarker = "PrivateMarker-For-Regression-Test";
        var ingestion = new IngestionCapture();
        using var http = new HttpClient(ingestion);
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = [] });
        builder.Logging.ClearProviders();
        HostedTelemetry.AddAzureMonitor(builder, options =>
        {
            options.ConnectionString = connection;
            options.Transport = new HttpClientTransport(http);
            options.DisableOfflineStorage = true;
        });
        var app = builder.Build();
        var provider = app.Services.GetRequiredService<TracerProvider>();
        string traceId;
        string ambientSpan;
        string requestSpan;
        using (var ambient = new Activity("ASP.NET ambient request").SetIdFormat(ActivityIdFormat.W3C).Start())
        {
            check(!ambient.Recorded, "Azure Monitor wire fixture starts with an unsampled ambient request");
            ambient.AddBaggage("private.request",privateMarker);
            traceId = ambient.TraceId.ToString();
            ambientSpan = ambient.SpanId.ToString();
            using var request = HostedTelemetry.Source.StartActivity("/attachment",ActivityKind.Server)
                ?? throw new InvalidOperationException("Hosted request span is unavailable.");
            requestSpan = request.SpanId.ToString();
            request.SetTag("http.response.status_code",200);
            await HostedTelemetry.DependencyAsync("Document Intelligence receipt", () => Task.FromResult(0));
            app.Services.GetRequiredService<ILoggerFactory>().CreateLogger(HostedTelemetry.Source.Name)
                .LogInformation("Capstone {Route} {Status} Trace={TraceId}","/attachment",200,traceId);
        }
        check(provider.ForceFlush(5000),"pinned Azure Monitor tracing processor flushes");
        await app.DisposeAsync();

        var entries = ingestion.Bodies.SelectMany(Envelopes).ToArray();
        var requestEnvelope = entries.Single(x => x.Kind == "Request" && x.Trace == traceId);
        var dependencyEnvelope = entries.Single(x => x.Kind == "RemoteDependency" && x.Trace == traceId);
        var logEnvelope = entries.Single(x => x.Kind == "Message" && x.Trace == traceId);
        Console.WriteLine(JsonSerializer.Serialize(new { traceId, requestSpan, requestEnvelope, dependencyEnvelope, logEnvelope }));
        check(requestEnvelope.Text == "/attachment" && requestEnvelope.Parent == ambientSpan &&
              dependencyEnvelope.Text == "Document Intelligence receipt" &&
              dependencyEnvelope.Parent == requestSpan &&
              logEnvelope.Parent == requestSpan && logEnvelope.Route == "/attachment",
            "real Azure Monitor exporter writes correlated request, dependency and log envelopes under an unsampled parent");
        check(ingestion.Bodies.All(body => !body.Contains(privateMarker,StringComparison.Ordinal)),
            "exported Azure Monitor envelopes omit the ambient private marker");
    }

    private static IEnumerable<Envelope> Envelopes(string body)
    {
        foreach (var line in body.Split('\n',StringSplitOptions.RemoveEmptyEntries))
        {
            using var json = JsonDocument.Parse(line);
            var entries = json.RootElement.ValueKind == JsonValueKind.Array
                ? json.RootElement.EnumerateArray().ToArray() : [json.RootElement];
            foreach (var entry in entries)
            {
                entry.TryGetProperty("tags",out var tags);
                var data = entry.GetProperty("data").GetProperty("baseData");
                data.TryGetProperty("properties",out var properties);
                yield return new(Text(entry,"name"),Text(tags,"ai.operation.id"),Text(tags,"ai.operation.parentId"),
                    Text(data,"name") ?? Text(data,"message"),Text(properties,"Route"));
            }
        }
    }

    private static string? Text(JsonElement element,string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name,out var value) &&
        value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private sealed record Envelope(string? Kind,string? Trace,string? Parent,string? Text,string? Route);

    private sealed class IngestionCapture : HttpMessageHandler
    {
        internal ConcurrentQueue<string> Bodies { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken)
        {
            if (request.RequestUri?.Host != "127.0.0.1")
                throw new InvalidOperationException("The Azure Monitor test transport accepts only its loopback target.");
            using var stream = await request.Content!.ReadAsStreamAsync(cancellationToken);
            using var decoded = request.Content.Headers.ContentEncoding.Contains("gzip")
                ? new GZipStream(stream,CompressionMode.Decompress) : null;
            using var reader = new StreamReader(decoded ?? stream);
            Bodies.Enqueue(await reader.ReadToEndAsync(cancellationToken));
            return new HttpResponseMessage(HttpStatusCode.OK)
                { Content = new StringContent("{\"itemsReceived\":1,\"itemsAccepted\":1,\"errors\":[]}") };
        }
    }
}
