using System.Net.Http.Json;
using System.Text.Json;

static class LocalJudge
{
    private sealed record Case(string Id, string Query, string Response, string Evidence, bool Supported, string Rationale);
    private sealed record Trial(string Rubric, string CaseId, int Seed, bool Reference, string Output, bool? Verdict);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static async Task Run()
    {
        var cases = JsonSerializer.Deserialize<Case[]>(File.ReadAllText("judge-cases-v1.json"), Json)
            ?? throw new InvalidDataException("Judge cases are missing.");
        using var client = new HttpClient { BaseAddress = new Uri("http://127.0.0.1:8413"), Timeout = TimeSpan.FromMinutes(1) };
        var trials = new List<Trial>();
        foreach (var rubric in new[] {"general", "evidence"})
        {
            var instruction = rubric == "general"
                ? "Judge if the support reply is acceptable for the customer's question. Reply YES or NO only."
                : "Use only the supplied evidence. Check every factual claim in the reply. Contradicted or missing details, including numbers, are NO. If all claims are explicitly supported, say YES. Reply YES or NO only.";
            foreach (var row in cases)
            {
                foreach (var seed in new[] {13, 41, 97})
                {
                    var body = new
                    {
                        messages = new[] {
                            new {role="system", content=instruction},
                            new {role="user", content=$"Question: {row.Query}\nEvidence: {row.Evidence}\nReply: {row.Response}"}
                        },
                        seed, temperature = 0.7, max_tokens = 12,
                    };
                    using var response = await client.PostAsJsonAsync("/v1/chat/completions", body);
                    response.EnsureSuccessStatusCode();
                    using var result = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                    var output = result.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "";
                    var token = output.Trim().TrimEnd('.','!').ToUpperInvariant();
                    bool? verdict = token switch {"YES" => true, "NO" => false, _ => null};
                    trials.Add(new(rubric, row.Id, seed, row.Supported, output, verdict));
                }
            }
            var items = trials.Where(t => t.Rubric == rubric).ToArray();
            var agreement = items.Count(t => t.Verdict == t.Reference);
            var unstable = items.GroupBy(t => t.CaseId).Count(g => g.Select(t => t.Verdict).Distinct().Count() > 1);
            var invalid = items.Count(t => t.Verdict is null);
            Console.WriteLine($"{rubric}: agreement {agreement}/{items.Length} | unstable cases {unstable}/{cases.Length} | invalid {invalid}");
        }
        File.WriteAllText("judge-trials-v1.json", JsonSerializer.Serialize(trials, Json));
        Console.WriteLine($"Local model calls: {trials.Count}");
    }
}
