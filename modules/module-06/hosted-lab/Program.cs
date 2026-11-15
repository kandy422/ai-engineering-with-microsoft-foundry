using System.Text.Json;
using System.Text.RegularExpressions;

if (args.Length != 2)
    throw new ArgumentException("Supply the manifest path and the Phase A recordings directory.");

var manifest = Read(args[0]);
var cases = manifest.GetProperty("calls").EnumerateArray().ToArray();
if (cases.Length != manifest.GetProperty("maxCalls").GetInt32() || cases.Length > 24)
    throw new InvalidDataException("Unexpected request count.");
var prior = new Dictionary<string, Observation>();
var observations = new List<Observation>();
foreach (var test in cases)
{
    var id = test.GetProperty("id").GetString()!;
    var lesson = test.GetProperty("lesson").GetString()!;
    var body = test.GetProperty("body");
    var observation = Inspect(args[1], id, lesson, body);
    if (test.TryGetProperty("condition", out var gate))
    {
        var extraction = prior[gate.GetProperty("requires").GetString()!];
        var fields = gate.GetProperty("expectedFields");
        var permitted = extraction.State == "completed" && extraction.Gate == "accepted"
            && extraction.OrderId == fields.GetProperty("orderId").GetString()
            && extraction.IssueType == fields.GetProperty("issueType").GetString();
        if (!permitted && observation.State != "missing")
            throw new InvalidDataException($"A recording exists for blocked conditional call {id}.");
        if (!permitted && extraction.State != "missing")
            observation = observation with { State = "gated" };
    }
    prior.Add(id, observation);
    observations.Add(observation);
}
var actions = new[] { "draft", "send" }.Select(action => new { action, allowed = Allowed(action) });
Console.WriteLine(JsonSerializer.Serialize(new { observations, localActions = actions },
    new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true }));

static Observation Inspect(string root, string id, string lesson, JsonElement body)
{
    var directory = Path.Combine(root, id);
    var row = new Observation(id, lesson, "missing");
    if (!File.Exists(Path.Combine(directory, "receipt.json"))) return row;

    var actual = Read(Path.Combine(directory, "request.json"));
    if (!JsonElement.DeepEquals(body, actual))
        throw new InvalidDataException($"Recorded request differs from reviewed manifest: {id}.");
    var receipt = Read(Path.Combine(directory, "receipt.json"));
    var status = receipt.TryGetProperty("httpStatus", out var http) ? http.GetInt32() : (int?)null;
    if (status != 200)
        return row with { State = "request-failed", Http = status };
    var response = Read(Path.Combine(directory, "response.json"));
    var model = response.GetProperty("model").GetString();
    if (model != "gpt-5-mini-2025-08-07")
        throw new InvalidDataException($"Unexpected model in recording {id}: {model}.");
    var choice = response.GetProperty("choices")[0];
    var reason = choice.GetProperty("finish_reason").GetString();
    var message = choice.GetProperty("message");
    var refusal = message.TryGetProperty("refusal", out var value) && value.ValueKind == JsonValueKind.String;
    var reply = message.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.String
        ? content.GetString() : null;
    var state = !refusal && reason == "stop" && !string.IsNullOrWhiteSpace(reply) ? "completed" : "incomplete";
    row = row with { State = state, Http = status, Model = model, Finish = reason, Reply = reply };
    if (state != "completed" || (lesson != "6.4" && !id.EndsWith("-extract", StringComparison.Ordinal))) return row;

    if (!TryReadTicket(reply!, out var orderId, out var issueType))
        return row with { Gate = "invalid shape" };
    var input = body.GetProperty("messages")[1].GetProperty("content").GetString()!;
    return row with { OrderId = orderId, IssueType = issueType, Gate = ValidateOrder(orderId, input) };
}

static bool Allowed(string action) => action == "draft";

static bool TryReadTicket(string reply, out string? orderId, out string? issueType)
{
    orderId = issueType = null;
    try
    {
        using var parsed = JsonDocument.Parse(reply);
        var value = parsed.RootElement;
        if (value.ValueKind != JsonValueKind.Object || value.EnumerateObject().Count() != 2
            || !value.TryGetProperty("orderId", out var id) || id.ValueKind is not (JsonValueKind.String or JsonValueKind.Null)
            || !value.TryGetProperty("issueType", out var issue) || issue.ValueKind != JsonValueKind.String)
            return false;
        orderId = id.GetString();
        issueType = issue.GetString();
        return issueType is "delivery" or "billing" or "review";
    }
    catch (JsonException)
    {
        return false;
    }
}

static string ValidateOrder(string? orderId, string input)
{
    if (orderId is null) return "missing ID";
    if (!Regex.Matches(input, @"\bORD-[0-9]{4}\b").Any(match => match.Value == orderId)) return "ID absent from ticket";
    return orderId == "ORD-1042" ? "accepted" : "order absent from store";
}

static JsonElement Read(string path)
{
    using var file = JsonDocument.Parse(File.ReadAllText(path));
    return file.RootElement.Clone();
}

record Observation(string Id, string Lesson, string State, int? Http = null, string? Model = null,
    string? Finish = null, string? Reply = null, string? OrderId = null, string? IssueType = null, string? Gate = null);

