using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

if (args.Length != 5 || args[0] is not ("check" or "send"))
    throw new ArgumentException("Usage: check|send <OpenAI-v1-base> <deployment> <request.json> <new-output-directory>");

var endpoint = GetChatEndpoint(args[1]);
var deployment = args[2];
if (deployment.Length is < 1 or > 64 || deployment.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '-' or '_')))
    throw new ArgumentException("Enter a valid Foundry deployment name.");

var payload = await File.ReadAllTextAsync(args[3]);
var body = JsonNode.Parse(payload)?.AsObject() ?? throw new InvalidDataException("Expected a request object.");
Validate(body, payload);
body["model"] = deployment;

var formatting = new JsonSerializerOptions { WriteIndented = true };
if (args[0] == "check")
{
    Console.WriteLine($"POST {endpoint.GetLeftPart(UriPartial.Path)}");
    Console.WriteLine(body.ToJsonString(formatting));
    return 0;
}

if (!Console.IsInputRedirected)
    throw new InvalidOperationException("Pipe the Entra access token from your signed-in Azure CLI.");
var token = Console.ReadLine();
if (string.IsNullOrWhiteSpace(token) || token.Any(char.IsWhiteSpace))
    throw new InvalidOperationException("No valid bearer token on standard input.");

var output = args[4];
Directory.CreateDirectory(output);
await WriteNew(Path.Combine(output, "request.json"), body.ToJsonString(formatting));
using var handler = new HttpClientHandler { AllowAutoRedirect = false };
using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(120) };
using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
var watch = Stopwatch.StartNew();
try
{
    using var response = await client.SendAsync(request);
    var content = await response.Content.ReadAsStringAsync();
    var status = (int)response.StatusCode;
    await WriteNew(Path.Combine(output, response.IsSuccessStatusCode ? "response.json" : "error.json"), content);
    JsonNode? reply;
    try { reply = JsonNode.Parse(content); }
    catch (JsonException) { reply = null; }
    var requestId = Header(response, "apim-request-id") ?? Header(response, "x-request-id");
    var receipt = new { httpStatus = status, requestId, elapsedMs = Math.Round(watch.Elapsed.TotalMilliseconds, 2),
        model = reply?["model"]?.GetValue<string>(), usage = reply?["usage"] };
    await WriteNew(Path.Combine(output, "receipt.json"), JsonSerializer.Serialize(receipt, formatting));
    Console.WriteLine($"HTTP {status} | evidence saved to {output}");
    return response.IsSuccessStatusCode ? 0 : 1;
}
catch (Exception error) when (error is HttpRequestException or TaskCanceledException)
{
    await WriteNew(Path.Combine(output, "receipt.json"),
        JsonSerializer.Serialize(new { errorType = error.GetType().Name, elapsedMs = watch.Elapsed.TotalMilliseconds }, formatting));
    Console.Error.WriteLine("Request failed before a complete HTTP response was received.");
    return 1;
}

static Uri GetChatEndpoint(string value)
{
    if (!Uri.TryCreate(value, UriKind.Absolute, out var baseUri) || baseUri.Scheme != "https" ||
        !baseUri.IsDefaultPort || baseUri.UserInfo.Length != 0 || baseUri.Query.Length != 0 || baseUri.Fragment.Length != 0 ||
        !(baseUri.Host.EndsWith(".openai.azure.com", StringComparison.OrdinalIgnoreCase)
          || baseUri.Host.EndsWith(".services.ai.azure.com", StringComparison.OrdinalIgnoreCase)) ||
        baseUri.AbsolutePath.TrimEnd('/') != "/openai/v1")
        throw new ArgumentException("Use the HTTPS OpenAI v1 base URL from your Foundry resource.");
    return new Uri(baseUri.GetLeftPart(UriPartial.Authority) + "/openai/v1/chat/completions");
}

static void Validate(JsonObject body, string payload)
{
    string[] allowed = ["model", "messages", "max_completion_tokens", "reasoning_effort", "response_format"];
    if (Encoding.UTF8.GetByteCount(payload) > 32768 || body.Any(field => !allowed.Contains(field.Key)))
        throw new InvalidDataException("Use a bounded Module 06 request without tools or streaming.");
    var cap = body["max_completion_tokens"]?.GetValue<int>() ?? 0;
    if (cap is < 1 or > 1024 || body["reasoning_effort"]?.GetValue<string>() != "minimal")
        throw new InvalidDataException("Use a completion cap from 1 to 1024 and minimal reasoning for this deployment.");
    if (body["messages"] is not JsonArray messages || messages.Count is < 1 or > 16 ||
        messages.Any(message => message is not JsonObject item || item.Count != 2 ||
            item["role"]?.GetValue<string>() is not ("system" or "user") ||
            string.IsNullOrWhiteSpace(item["content"]?.GetValue<string>())))
        throw new InvalidDataException("Use only bounded system and user text messages.");
    if (body.TryGetPropertyValue("response_format", out var format) &&
        (format is not JsonObject || format["type"]?.GetValue<string>() != "json_schema" ||
         format["json_schema"]?["strict"]?.GetValue<bool>() != true || format["json_schema"]?["schema"] is not JsonObject))
        throw new InvalidDataException("Supply the strict JSON-schema response format from the lab.");
}

static string? Header(HttpResponseMessage response, string name) =>
    response.Headers.TryGetValues(name, out var values) ? values.FirstOrDefault() : null;

static async Task WriteNew(string file, string contents)
{
    await using var stream = new FileStream(file, FileMode.CreateNew, FileAccess.Write, FileShare.None);
    await using var writer = new StreamWriter(stream, new UTF8Encoding(false));
    await writer.WriteAsync(contents);
}

