using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;

if (args.Length > 0)
{
    if (args.Length > 2)
        throw new ArgumentException("Expected a checkpoint and at most one variant.");
    await Checkpoints.Run(args[0], args.Length == 2 ? args[1] : null);
    return;
}

const string question = "How do I reset my password?";
const string passage = "Open Account Settings and choose Reset password.";
const string source = "KB-RESET";
const string runtime = "local-runtime";

var hits = new ConcurrentQueue<string>();
var storageCalls = 0;
var searchAllowed = true;
using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
await using var storage = Service();
storage.MapGet("/document", () =>
{
    Interlocked.Increment(ref storageCalls);
    return new { passage, source };
});
await storage.StartAsync();

await using var search = Service();
search.MapPost("/search", (Question input, HttpContext context) =>
{
    hits.Enqueue("Search");
    return searchAllowed && context.Request.Headers["X-Lab-Identity"] == runtime
        && input.Text == question
        ? Results.Ok(new Evidence(passage, source))
        : Results.StatusCode(403);
});
await search.StartAsync();

await using var model = Service();
model.MapPost("/generate", (GroundedPrompt input, HttpContext context) =>
{
    hits.Enqueue("Model");
    return context.Request.Headers["X-Lab-Identity"] == runtime && input.Question == question
        ? Results.Ok(new Answer(input.Evidence.Passage, input.Evidence.Source))
        : Results.StatusCode(403);
});
await model.StartAsync();

await using var backend = Service();
backend.MapPost("/ask", async (Question input) =>
{
    hits.Enqueue("Backend");
    using var searchRequest = Request(search, "/search", input);
    using var searchResult = await client.SendAsync(searchRequest);
    if (!searchResult.IsSuccessStatusCode)
        return Results.StatusCode(503);
    var evidence = await searchResult.Content.ReadFromJsonAsync<Evidence>();
    using var modelRequest = Request(model, "/generate", new GroundedPrompt(input.Text, evidence!));
    using var modelResult = await client.SendAsync(modelRequest);
    return modelResult.IsSuccessStatusCode
        ? Results.Ok(await modelResult.Content.ReadFromJsonAsync<Answer>())
        : Results.StatusCode(503);
});
await backend.StartAsync();

Console.WriteLine("POST /ask");
Console.WriteLine(question);
using var normal = await client.PostAsJsonAsync(backend.Urls.Single() + "/ask", new Question(question));
var answer = await normal.Content.ReadFromJsonAsync<Answer>();
var successfulPath = string.Join(" -> ", hits);
Console.WriteLine($"HTTP {(int)normal.StatusCode} | {successfulPath}");
Console.WriteLine($"{answer?.Text} [{answer?.Source}]");
Console.WriteLine($"Storage HTTP calls: {storageCalls}");
hits.Clear();
searchAllowed = false;
using var denied = await client.PostAsJsonAsync(backend.Urls.Single() + "/ask", new Question(question));
var deniedPath = string.Join(" -> ", hits);
Console.WriteLine($"HTTP {(int)denied.StatusCode} | {deniedPath}");
if (normal.StatusCode != HttpStatusCode.OK || answer != new Answer(passage, source) ||
    successfulPath != "Backend -> Search -> Model" || storageCalls != 0 ||
    denied.StatusCode != HttpStatusCode.ServiceUnavailable || deniedPath != "Backend -> Search")
    throw new InvalidOperationException("The observed trace differs from the expected trace.");

static WebApplication Service()
{
    var builder = WebApplication.CreateSlimBuilder();
    builder.Logging.ClearProviders();
    builder.WebHost.UseUrls("http://127.0.0.1:0");
    return builder.Build();
}

static HttpRequestMessage Request<T>(WebApplication app, string path, T body)
{
    var request = new HttpRequestMessage(HttpMethod.Post, app.Urls.Single() + path);
    request.Headers.Add("X-Lab-Identity", runtime);
    request.Content = JsonContent.Create(body);
    return request;
}

record Question(string Text);
record Evidence(string Passage, string Source);
record GroundedPrompt(string Question, Evidence Evidence);
record Answer(string Text, string Source);
