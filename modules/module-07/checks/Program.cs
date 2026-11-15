using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

const string input = "Where is order A-103?";
var endpoint = new Uri("https://fixture.example/openai/v1/");
var messages = new[] {new Turn("user", input)};
using var handler = new FixtureHandler(input);
using var http = new HttpClient(handler);
var model = new FoundryChat(http, endpoint, "support-lab", _ => Task.FromResult("fixture-token"));
var items = new List<ModelEvent>();
await foreach (var item in model.StreamAsync(messages, "normal", CancellationToken.None))
    items.Add(item);
Assert(handler.Calls == 2, "A throttled first attempt must retry exactly once.");
Assert(string.Concat(items.Select(i => i.Delta)) == "Please share your order reference.", "Expected the fixture text.");
Assert(items.Single(i => i.Usage != null).Usage == new TokenUsage(11, 6), "Expected fixture usage parsed separately.");

using var blockedHandler = new FixtureHandler(input, alwaysThrottle: true);
using var blockedHttp = new HttpClient(blockedHandler);
var blocked = new FoundryChat(blockedHttp, endpoint, "support-lab", _ => Task.FromResult("fixture-token"));
try
{
    await foreach (var item in blocked.StreamAsync(messages, "normal", CancellationToken.None)) { }
    throw new InvalidOperationException("The persistent 429 must fail.");
}
catch (HttpRequestException error) when (error.StatusCode == HttpStatusCode.TooManyRequests)
{
    Assert(blockedHandler.Calls == 2, "Persistent 429 must stop after one retry.");
}
using var singleHandler = new FixtureHandler(input, alwaysThrottle: true, expectedEffort: "minimal");
using var singleHttp = new HttpClient(singleHandler);
var single = new FoundryChat(singleHttp, endpoint, "support-lab", _ => Task.FromResult("fixture-token"),
    new FoundryChatOptions(RetryOnThrottle: false, ReasoningEffort: "minimal"));
try
{
    await foreach (var item in single.StreamAsync(messages, "normal", CancellationToken.None)) { }
    throw new InvalidOperationException("The capture profile must report 429.");
}
catch (HttpRequestException error) when (error.StatusCode == HttpStatusCode.TooManyRequests)
{
    Assert(singleHandler.Calls == 1, "The capture profile must issue at most one attempt.");
}
Console.WriteLine(JsonSerializer.Serialize(new
{
    mode = "In-process HTTP fixture, no cloud request", input, recoveredAfterAttempts = handler.Calls,
    reply = string.Concat(items.Select(i => i.Delta)), fixtureUsage = items.Single(i => i.Usage != null).Usage,
    stoppedAfterAttempts = blockedHandler.Calls, captureProfileAttempts = singleHandler.Calls, result = "PASS"
}));

static void Assert(bool condition, string failure)
{
    if (!condition) throw new InvalidOperationException(failure);
}

sealed class FixtureHandler(string input, bool alwaysThrottle = false, string? expectedEffort = null) : HttpMessageHandler
{
    public int Calls { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellation)
    {
        Calls++;
        if (request.RequestUri?.AbsoluteUri != "https://fixture.example/openai/v1/chat/completions" ||
            !Equals(request.Headers.Authorization, new AuthenticationHeaderValue("Bearer", "fixture-token")))
            throw new InvalidOperationException("Wrong route or authorization.");
        using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellation));
        var body = json.RootElement;
        var actualEffort = body.TryGetProperty("reasoning_effort", out var effort) ? effort.GetString() : null;
        if (actualEffort != expectedEffort || body.GetProperty("max_completion_tokens").GetInt32() != 256)
            throw new InvalidOperationException("Incorrect reasoning setting or token cap.");
        var messages = body.GetProperty("messages");
        var user = messages[1];
        if (messages[0].GetProperty("role").GetString() != "system")
            throw new InvalidOperationException("Trusted support instructions must precede the user turn.");
        if (body.GetProperty("model").GetString() != "support-lab" ||
            !body.GetProperty("stream_options").GetProperty("include_usage").GetBoolean() ||
            user.GetProperty("role").GetString() != "user" || user.GetProperty("content").GetString() != input)
            throw new InvalidOperationException("Wrong deployment, settings or input.");
        if (Calls == 1 || alwaysThrottle)
        {
            var busy = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            busy.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.Zero);
            return busy;
        }
        const string sse = """
            data: {"choices":[{"delta":{"content":"Please share your order reference."},"finish_reason":null}]}

            data: {"choices":[{"delta":{},"finish_reason":"stop"}]}

            data: {"choices":[],"usage":{"prompt_tokens":11,"completion_tokens":6}}

            data: [DONE]
            """;
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(sse, System.Text.Encoding.UTF8, "text/event-stream")
        };
    }
}



