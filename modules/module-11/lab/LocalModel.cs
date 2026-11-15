
using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;

sealed class LocalModel(string toolSchema, Func<Caller, string, OrderResult> lookup) : IDisposable
{
    private readonly HttpClient client = new()
    {
        BaseAddress = new("http://127.0.0.1:8411/"),
        Timeout = TimeSpan.FromSeconds(60)
    };

    public async Task<ModelRun> OrderAsync(Caller caller, string prompt, Action<string, object> log,
        bool specialist = false, bool labelsOnly = false)
    {
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        using var schema = JsonDocument.Parse(toolSchema);
        var definition = schema.RootElement;
        object[] tools = [new
        {
            type = "function",
            function = new { name = definition.GetProperty("name"), description = definition.GetProperty("description"), parameters = definition.GetProperty("inputSchema") }
        }];
        var instructions = "You handle order status questions. For a question about a specific order, use lookup_order. Report the order status using the tool result. For a not_available result, say Escalate. Never guess.";

        var messages = new List<object>
        {
            new { role = "system", content = instructions },
            new { role = "user", content = prompt }
        };
        var stopwatch = Stopwatch.StartNew();
        var decision = await AskAsync(messages, tools, "auto", log, budget.Token);
        if (!decision.TryGetProperty("tool_calls", out var calls) || calls.GetArrayLength() != 1)
            {
            var skipped = decision.TryGetProperty("content", out var content) ? content.GetString()?.Trim() ?? "" : "";
            log("no-tool-call", new { skipped, elapsedMilliseconds = stopwatch.Elapsed.TotalMilliseconds });
            return new(new("no_tool_call"), skipped, stopwatch.Elapsed.TotalMilliseconds, 1, 0);
        }
        var call = calls[0];
        var fn = call.GetProperty("function");
        var name = fn.GetProperty("name").GetString();
        var arguments = fn.GetProperty("arguments");
        var callId = call.GetProperty("id").GetString();
        var result = name == "lookup_order"
            ? lookup(caller, arguments.ValueKind == JsonValueKind.String ? arguments.GetString()! : arguments.GetRawText())
            : new("unknown_tool");
        log("live-tool-call", new { callId, name, arguments });
        log("live-tool-result", new { callId, result });
        messages.Add(decision);
        messages.Add(new { role = "tool", tool_call_id = callId, content = JsonSerializer.Serialize(result) });

        if (labelsOnly)
            messages[0] = new { role = "system", content = instructions + " After the lookup reply with one word only: Shipped, Processing, or Escalate." };
        var modelCalls = 2;
        JsonElement final;
        if (specialist && result.Code == "not_available")
        {
            // Only the minimal public result crosses this handoff.
            object[] handoff = [
                new {role="system",content="You triage support lookups. For not_available, reply with the single word Escalate."},
                new {role="user",content=JsonSerializer.Serialize(result)}
            ];
            log("handoff", new { to = "Escalation specialist", result });
            var recommendation = await AskAsync(handoff, null, null, log, budget.Token);
            var decisionLabel = recommendation.GetProperty("content").GetString()?.Trim();
            messages.Add(new { role = "user", content = $"Escalation specialist recommendation: {decisionLabel}. Confirm using the lookup result." });
            final = await AskAsync(messages, tools, "none", log, budget.Token);
            modelCalls++;
        }
        else
        {
            final = await AskAsync(messages, tools, "none", log, budget.Token);
        }
        var answer = final.GetProperty("content").GetString()?.Trim() ?? "";
        log("live-reply", new { answer, elapsedMilliseconds = stopwatch.Elapsed.TotalMilliseconds });
        return new(result, answer, stopwatch.Elapsed.TotalMilliseconds, modelCalls, 1);
    }

    private async Task<JsonElement> AskAsync(IEnumerable<object> messages, object[]? tools, string? toolChoice,
        Action<string, object> log, CancellationToken cancellationToken)
    {
        var request = new Dictionary<string, object>
        {
            ["model"] = "Qwen3-0.6B-Q8_0",
            ["messages"] = messages,
            ["temperature"] = 0,
            ["seed"] = 17,
            ["cache_prompt"] = false,
            ["max_tokens"] = 200,
            ["chat_template_kwargs"] = new { enable_thinking = false }
        };
        if (tools is not null)
        {
            request["tools"] = tools;
            request["tool_choice"] = toolChoice!;
        }
        log("model-request", request);
        using var response = await client.PostAsJsonAsync("v1/chat/completions", request, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            log("model-http-error", new { status = (int)response.StatusCode, responseBody });
        response.EnsureSuccessStatusCode();
        using var raw = JsonDocument.Parse(responseBody);
        log("model-response", raw.RootElement);
        return raw.RootElement.GetProperty("choices")[0].GetProperty("message").Clone();
    }

    public void Dispose() => client.Dispose();
}

record ModelRun(OrderResult Result, string Answer, double ElapsedMilliseconds, int ModelCalls, int LookupCalls);




