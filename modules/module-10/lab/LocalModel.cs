using System.Net.Http.Json;
using System.Text.Json;

static class LocalModel
{
    private static readonly HttpClient Client = new()
    {
        BaseAddress = new Uri("http://127.0.0.1:8410"),
        Timeout = TimeSpan.FromMinutes(2)
    };
    public record Evidence(string Id, string Text);
    public record Request(string Question, Evidence[] Evidence);
    public record Run(Request Input, JsonElement Payload, JsonElement Response, string Output);

    public static async Task<Run> Complete(Request input, string? system = null)
    {
        const string instruction =
            "You answer customer support questions. Use only the supplied policy passages. " +
            "If no passage supports the requested policy, answer: I cannot verify the return window. " +
            "If supported, answer in one short sentence and cite the passage ID in square brackets.";
        var passageText = input.Evidence.Length == 0 ? "(none)"
            : string.Join("\n", input.Evidence.Select(p => $"[{p.Id}] {p.Text}"));
        var payload = JsonSerializer.SerializeToElement(new
        {
            model = "qwen2.5-1.5b-instruct",
            temperature = 0,
            seed = 17,
            max_tokens = 96,
            messages = new[] {
                new { role = "system", content = system ?? instruction },
                new { role = "user", content = $"Question: {input.Question}\nPolicy passages:\n{passageText}" }
            }
        });
        using var response = await Client.PostAsJsonAsync("/v1/chat/completions", payload);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        var answer = result.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();
        return new Run(input, payload, result, answer ?? throw new InvalidOperationException("Empty model answer"));
    }

    public static async Task<object> Demo(string lesson)
    {
        const string question = "What is the return window for an unopened accessory?";
        var policy = Pipeline.Load("lab/policies");
        Evidence Get(string id)
        {
            var passage = policy.Single(p => p.Id == id);
            return new Evidence(passage.Id, passage.Text);
        }
        if (lesson == "101")
        {
            var none = await Complete(new Request(question, []));
            var shipping = await Complete(new Request(question, [Get("shipping#standard")]));
            var returns = await Complete(new Request(question, [Get("returns#unopened")]));
            return new[] { none, shipping, returns };
        }
        if (lesson == "103")
        {
            var full = Pipeline.Prepare(question, policy, 24);
            var small = Pipeline.Prepare(question, policy, 5);
            Request ToModel(Pipeline.Request request) =>
                new(request.Question, request.Context.Select(p => new Evidence(p.Id, p.Text)).ToArray());
            return new { full = await Complete(ToModel(full)), small = await Complete(ToModel(small)) };
        }
        if (lesson is "106" or "107")
        {
            var followup = lesson == "106";
            var userQuestion = followup
                ? "Under what condition is an opened accessory eligible for a return?"
                : question;
            var rawQuery = followup ? "What if it was opened?" : "sealed add-on";
            var improvedQuery = followup ? "accessory return opened" : "sealed add-on accessory return unopened";
            Request Build(string searchQuery) => new(userQuestion,
                Pipeline.Prepare(searchQuery, policy, 40).Context.Select(p => new Evidence(p.Id, p.Text)).ToArray());
                        string? instruction = followup
                ? "Answer the product eligibility question using only the relevant policy passage. State the condition and cite its ID in square brackets. If there is no supporting passage, say the policy cannot be verified."
                : null;
            return new { rawQuery, improvedQuery, before = await Complete(Build(rawQuery), instruction),
                after = await Complete(Build(improvedQuery), instruction) };
        }
        throw new ArgumentException("Unsupported model checkpoint");
    }
}


