using System.Net.Http.Json;
using System.Text.Json;

static class Routing
{
    const string Manual = "MANUAL_REVIEW";
    static readonly HashSet<string> Queues = new(StringComparer.OrdinalIgnoreCase)
        { "BILLING", "DELIVERY", "RETURNS" };

    static string Normalize(string? answer)
    {
        var label = answer?.Trim() ?? "";
        return Queues.Contains(label) ? label.ToUpperInvariant() : Manual;
    }

    static async Task<string> Route(HttpClient http, string model, string ticket, bool alignedPrompt)
    {
        if (string.IsNullOrWhiteSpace(ticket)) return Manual;
        try
        {
            using var response = await http.PostAsJsonAsync("v1/chat/completions", new
            {
                model,
                messages = alignedPrompt
                    ? new[] { new { role = "user", content = "Route the support ticket. Reply with one label: BILLING, DELIVERY, or RETURNS. Ticket: " + ticket } }
                    : new[]
                    {
                        new { role = "system", content = "Route the customer ticket. Return exactly one label: BILLING, DELIVERY or RETURNS." },
                        new { role = "user", content = ticket }
                    },
                temperature = 0, seed = 42, max_tokens = 48
            });
            response.EnsureSuccessStatusCode();
            var reply = await response.Content.ReadFromJsonAsync<JsonElement>();
            return Normalize(reply.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString());
        }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException or JsonException
            or KeyNotFoundException or IndexOutOfRangeException or InvalidOperationException)
        {
            return Manual;
        }
    }

    public static async Task Smoke(string[] args, JsonSerializerOptions options)
    {
        using var http = new HttpClient { BaseAddress = new Uri(args[0]), Timeout = TimeSpan.FromSeconds(15) };
        var model = args[1];
        var alignedPrompt = args.Length > 2 && args[2] == "v2";
        var cases = new[]
        {
            (Name: "duplicate-charge", Input: "One purchase appeared on my card statement twice.", Expected: "BILLING"),
            (Name: "lost-parcel", Input: "The carrier says my parcel arrived but it is missing.", Expected: "DELIVERY"),
            (Name: "blank-ticket", Input: "", Expected: Manual)
        };
        var rows = new List<object>();
        var success = true;
        foreach (var test in cases)
        {
            var actual = await Route(http, model, test.Input, alignedPrompt);
            var pass = actual == test.Expected;
            success &= pass;
            rows.Add(new { test.Name, test.Input, test.Expected, actual, pass });
        }
        var fallback = Normalize("UNEXPECTED_QUEUE");
        success &= fallback == Manual;
        rows.Add(new { Name = "invalid-output-fixture", Input = "UNEXPECTED_QUEUE", Expected = Manual, actual = fallback, pass = fallback == Manual });
        Console.WriteLine(JsonSerializer.Serialize(new { model, prompt = alignedPrompt ? "v2" : "v1", success, rows }, options));
        if (!success) Environment.ExitCode = 2;
    }
}

