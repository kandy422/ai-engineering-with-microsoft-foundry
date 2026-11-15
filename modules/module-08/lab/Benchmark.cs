using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;

static class Benchmark
{
    public static async Task Run(string[] args, JsonSerializerOptions options)
    {
        var cases = JsonSerializer.Deserialize<TestCase[]>(File.ReadAllText(args[0]), options)!;
        var endpoint = new Uri(args[1]);
        if (args.Length != 4 || !endpoint.IsLoopback)
            throw new ArgumentException("Use a loopback endpoint for the local benchmark. Foundry uses benchmark-foundry-plan and benchmark-foundry-run.");
        using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { BaseAddress = endpoint, Timeout = TimeSpan.FromMinutes(2) };
        var model = args[2];
        var trials = int.Parse(args[3]);
        var rows = new List<object>();

        async Task<JsonElement> Complete(string prompt)
        {
            var request = new Dictionary<string, object>
            {
                ["model"] = model,
                ["messages"] = new[] { new { role = "user", content = prompt } },
                ["temperature"] = 0,
                ["max_tokens"] = 48,
                ["seed"] = 42,
                ["cache_prompt"] = false
            };
            using var result = await http.PostAsJsonAsync("v1/chat/completions", request);
            result.EnsureSuccessStatusCode();
            return await result.Content.ReadFromJsonAsync<JsonElement>();
        }

        await Complete("Reply with the word ready.");
        for (int trial = 1; trial <= trials; trial++)
        {
            foreach (var test in cases)
            {
                var timer = Stopwatch.StartNew();
                var result = await Complete(test.Prompt);
                timer.Stop();
                var message = result.GetProperty("choices")[0].GetProperty("message");
                var answer = message.GetProperty("content").GetString()!.Trim();
                var usage = result.GetProperty("usage");
                rows.Add(new {
                    test.Id, test.Task, test.Expected, test.Prompt, trial, answer,
                    pass = string.Equals(answer, test.Expected, StringComparison.OrdinalIgnoreCase),
                    elapsedMs = Math.Round(timer.Elapsed.TotalMilliseconds, 1),
                    inputTokens = usage.GetProperty("prompt_tokens").GetInt32(),
                    outputTokens = usage.GetProperty("completion_tokens").GetInt32()
                });
            }
        }

        Console.WriteLine(JsonSerializer.Serialize(new { model, trials, rows }, options));
    }

    internal record TestCase(string Id, string Task, string Prompt, string Expected);
}


