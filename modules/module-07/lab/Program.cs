using System.Diagnostics;
using System.Net;
using System.Text.Json;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

var builder = WebApplication.CreateBuilder(args);
var endpoint = builder.Configuration["Foundry:Endpoint"];
var deployment = builder.Configuration["Foundry:Deployment"];
var local = string.IsNullOrEmpty(endpoint) && string.IsNullOrEmpty(deployment);

if (local) builder.Services.AddSingleton<IChatModel, LocalStub>();
else
{
    if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) || uri.Scheme != "https"
        || !(uri.Host.EndsWith(".openai.azure.com") || uri.Host.EndsWith(".services.ai.azure.com"))
        || uri.AbsolutePath != "/openai/v1/" || uri.Query.Length != 0 || uri.UserInfo.Length != 0
        || uri.Fragment.Length != 0 || !uri.IsDefaultPort || string.IsNullOrWhiteSpace(deployment))
        throw new InvalidOperationException("Set Foundry endpoint ending in /openai/v1/ and deployment name.");
    var capturePlan = builder.Configuration["Foundry:CapturePlan"];
    var captureDirectory = builder.Configuration["Foundry:CaptureDirectory"];
    HttpMessageHandler handler = capturePlan is null
        ? new HttpClientHandler { AllowAutoRedirect = false }
        : new RecordingGuard(capturePlan, captureDirectory ?? throw new InvalidOperationException("Capture directory is required."));
    builder.Services.AddSingleton<IChatModel>(new FoundryChat(
        new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan },
        uri, deployment, options: new FoundryChatOptions(
            RetryOnThrottle: builder.Configuration.GetValue("Foundry:RetryOnThrottle", true),
            ReasoningEffort: builder.Configuration["Foundry:ReasoningEffort"],
            Subscription: builder.Configuration["Foundry:Subscription"])));
}
var permitLimit = builder.Configuration.GetValue("Chat:PermitLimit", 12);
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = 429;
    options.AddFixedWindowLimiter("chat", limiter =>
    {
        limiter.PermitLimit = permitLimit;
        limiter.Window = TimeSpan.FromMinutes(1);
        limiter.QueueLimit = 0;
    });
});
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 32_768);

var app = builder.Build();
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseRateLimiter();
app.MapGet("/mode", () => new { mode = local ? "Local stub" : "Microsoft Foundry" });
app.MapPost("/chat", async (ChatRequest request, HttpContext context, IChatModel model, ILogger<Program> log) =>
{
    var turns = request.Messages;
    if (turns is null || turns.Length is < 1 or > 15 || turns.Length % 2 == 0 ||
        turns.Where((turn, i) => turn is null || turn.Role != (i % 2 == 0 ? "user" : "assistant") ||
            string.IsNullOrWhiteSpace(turn.Content) || turn.Content.Length > 1500).Any() ||
        request.Scenario is not ("normal" or "throttle" or "timeout") ||
        (!local && request.Scenario != "normal"))
    {
        context.Response.StatusCode = 400;
        return;
    }

    var watch = Stopwatch.StartNew();
    var outcome = "done";
    var correlationId = context.TraceIdentifier;
    context.Response.ContentType = "application/x-ndjson";
    context.Response.Headers.CacheControl = "no-store";
    context.Response.Headers["X-Correlation-Id"] = correlationId;
    using var deadline = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
    deadline.CancelAfter(TimeSpan.FromSeconds(local ? 5 : 20));

    async Task SendAsync(string kind, string text)
    {
        await context.Response.WriteAsync(JsonSerializer.Serialize(new { kind, text }) + "\n", context.RequestAborted);
        await context.Response.Body.FlushAsync(context.RequestAborted);
    }

    try
    {
        await SendAsync("started", correlationId);
        await foreach (var item in model.StreamAsync(turns, request.Scenario, deadline.Token))
        {
            if (item.Delta is { } delta) await SendAsync("delta", delta);
            if (item.Usage is { } usage)
                log.LogInformation("Chat {CorrelationId} used {InputTokens} input tokens and {OutputTokens} output tokens",
                    correlationId, usage.InputTokens, usage.OutputTokens);
        }
        await SendAsync("done", "");
    }
    catch (Exception) when (context.RequestAborted.IsCancellationRequested)
    {
        outcome = "cancelled";
    }
    catch (OperationCanceledException) when (deadline.IsCancellationRequested)
    {
        outcome = "timeout";
        await SendAsync("error", "The request timed out. Try again.");
    }
    catch (HttpRequestException e) when (e.StatusCode == HttpStatusCode.TooManyRequests)
    {
        outcome = "throttled";
        await SendAsync("error", "The service is busy. Try again shortly.");
    }
    catch (Exception e) when (e is HttpRequestException or IOException or JsonException)
    {
        outcome = e is HttpRequestException { StatusCode: { } status } ? $"http-{(int)status}" : "failed";
        await SendAsync("error", "Could not complete the reply. Check the server log using the request ID.");
    }
    finally
    {
        log.LogInformation("Chat {CorrelationId} {Outcome} in {ElapsedMs}ms", correlationId, outcome, watch.ElapsedMilliseconds);
    }
}).RequireRateLimiting("chat");
app.Run();



