using System.Text.Json;

static class Checkpoints
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static T Read<T>(string file) => JsonSerializer.Deserialize<T>(File.ReadAllText(file), Json)
        ?? throw new InvalidDataException(file);

    public static void Run(string lesson)
    {
        var cases = Read<EvalCase[]>("cases-v1.json");
        switch (lesson)
        {
            case "13.2":
                Dataset(cases);
                break;
            case "13.3":
                Retrieval(cases);
                break;
            case "13.4":
                Calibration();
                break;
            case "13.5":
                Traces();
                break;
            case "13.6":
                Experiment(cases);
                break;
            case "13.7":
                Regression(cases);
                break;
            default:
                throw new ArgumentException($"Unknown checkpoint {lesson}");
        }
    }

    private static void Dataset(EvalCase[] cases)
    {
        if (cases.Select(c => c.Id).Distinct().Count() != cases.Length)
            throw new InvalidDataException("Duplicate case IDs.");
        if (cases.Any(c => c.Split is not ("dev" or "holdout")))
            throw new InvalidDataException("Unknown split label.");
        var leaked = cases.GroupBy(c => c.Family)
            .Count(group => group.Select(c => c.Split).Distinct().Count() > 1);
        if (leaked > 0) throw new InvalidDataException($"Cross-split families: {leaked}");
        foreach (var c in cases)
            Console.WriteLine($"{c.Id} | {c.Split} | {c.Slice} | {c.ExpectedOutcome}");
        Console.WriteLine($"Cross-split families: {leaked}");
    }

    private static void Retrieval(EvalCase[] cases)
    {
        foreach (var row in Read<RagRow[]>("rag-v1.json"))
        {
            var expected = cases.Single(c => c.Id == row.Id).ExpectedEvidence;
            var recall = (double)row.Ranking.Take(2).Intersect(expected).Count() / expected.Length;
            var rank = Array.FindIndex(row.Ranking, id => expected.Contains(id)) + 1;
            var reciprocalRank = rank == 0 ? 0 : 1d / rank;
            var cited = expected.Contains(row.Citation);
            var correct = row.Answer == row.Reference;
            Console.WriteLine(FormattableString.Invariant($"{row.Id} Recall@2={recall:F2} RR={reciprocalRank:F2}"));
            Console.WriteLine($"{row.Id} ExpectedDocumentCited={cited} ExactReferenceMatch={correct}");
        }
    }

    private static void Calibration()
    {
        // Authored labels and deterministic stand-ins for a grader's output contract.
        (string Id, string Claim, string Evidence, bool Reference)[] rows =
        [
            ("pol-01", "60 days", "Domestic returns are accepted within 30 days.", false),
            ("pol-03", "30 days", "Domestic returns are accepted within 30 days.", true),
            ("ord-01", "escalate", "Escalate if dispatched.", true)
        ];
        var loose = rows.Count(row => (row.Claim.Length > 0) == row.Reference);
        bool QuoteCheck(string claim, string evidence) =>
            evidence.Contains(claim, StringComparison.OrdinalIgnoreCase);
        var calibrated = rows.Count(row => QuoteCheck(row.Claim, row.Evidence) == row.Reference);
        var repeated = rows.All(row => Enumerable.Range(0, 5)
            .Select(_ => QuoteCheck(row.Claim, row.Evidence)).Distinct().Count() == 1);
        Console.WriteLine($"Authored reference labels: {rows.Length}");
        Console.WriteLine($"Loose stub agreement: {loose}/{rows.Length}");
        Console.WriteLine($"Quote-check stub agreement: {calibrated}/{rows.Length}");
        Console.WriteLine($"Five identical inputs per row: stable={repeated}");
        Console.WriteLine("Live judge calls: 0");
    }

    private static void Traces()
    {
        foreach (var trace in Read<Trace[]>("traces-v1.json"))
        {
            string diagnosis;
            if (trace.Steps.Any(step => step.OrderId != "A17")
                || !trace.Steps.Select(step => step.Tool).SequenceEqual(["get_order","cancel_order"]))
                diagnosis = "planning/arguments";
            else if (trace.Steps.Any(step => step.Status != "OK"))
                diagnosis = "tool execution";
            else
                diagnosis = "task completed";
            Console.WriteLine($"{trace.Id} | calls={trace.Steps.Length} | {diagnosis}");
        }
    }

    private static void Experiment(EvalCase[] cases)
    {
        var policies = Read<Policy[]>("experiment-policies-v1.json");
        foreach (var version in new[] {"keyword", "scoped-keyword"})
        {
            var file = $"foundry-{version}-v1.jsonl";
            var rows = new List<string>();
            foreach (var c in cases.Where(c => c.Split == "holdout"))
            {
                var words = Tokens(c.Query);
                var scope = words.Contains("abroad") ? "crossborder-return"
                    : words.Contains("shipped") ? "dispatched-cancel"
                    : words.Contains("processing") ? "processing-cancel"
                    : "domestic-return";
                var candidates = version == "keyword" ? policies : policies.Where(p => p.Scope == scope);
                var winner = candidates.Select(p => (Policy:p, Score:words.Intersect(Tokens(p.Text)).Count()))
                    .Where(item => item.Score > 0).OrderByDescending(item => item.Score)
                    .ThenBy(item => item.Policy.Id, StringComparer.Ordinal).FirstOrDefault().Policy;
                var response = winner?.Outcome ?? "ask-for-policy";
                var evidence = winner is null ? Array.Empty<string>() : new[] {winner.Id};
                var match = response == c.ExpectedOutcome && evidence.SequenceEqual(c.ExpectedEvidence);
                Console.WriteLine($"{version} | {c.Id} | {winner?.Id ?? "no evidence"} | {(match ? "MATCH" : "MISMATCH")}");
                rows.Add(JsonSerializer.Serialize(new {caseId=c.Id, c.Query, c.Slice,
                    response, reference=c.ExpectedOutcome, evidence, context=winner?.Text ?? "", version}, Json));
            }
            File.WriteAllLines(file, rows);
        }
        Console.WriteLine("Cloud submissions: 0");
    }

    private static string[] Tokens(string value) =>
        System.Text.RegularExpressions.Regex.Matches(value.ToLowerInvariant(), "[a-z0-9]+")
            .Select(m => m.Value).Distinct().ToArray();
    private static void Regression(EvalCase[] cases)
    {
        foreach (var version in new[] {"baseline", "targeted", "blanket-ban"})
            ReportRegression(version, cases);
    }

    public static int Gate(string version)
    {
        if (version is not ("baseline" or "targeted" or "blanket-ban"))
            throw new ArgumentException($"Unknown version {version}.");
        return ReportRegression(version, Read<EvalCase[]>("cases-v1.json")) ? 0 : 1;
    }

    private static bool ReportRegression(string version, EvalCase[] cases)
    {
        var holdout = cases.Where(c => c.Split == "holdout").ToArray();
        if (holdout.Length == 0) throw new InvalidDataException("A release gate requires holdout cases.");
        var failures = holdout.Where(c =>
        {
            var result = Decide(version,c);
            return result.Outcome != c.ExpectedOutcome || !result.Actions.All(c.AllowedActions.Contains);
        }).Select(c => c.Id).ToArray();
        var passed = failures.Length == 0;
        Console.WriteLine($"{version} | {(passed ? "PASS" : "BLOCK")} | failures={string.Join(',',failures)}");
        return passed;
    }
    private static Decision Decide(string version, EvalCase c)
    {
        // A deterministic application boundary fixture, with no model or external order service.
        if (c.Family == "dispatched-cancel")
            return new("escalate", version == "baseline" ? ["cancel_order"] : []);
        if (c.Family == "processing-cancel")
            return version == "blanket-ban" ? new("escalate",[]) : new("cancel",["cancel_order"]);
        if (c.Family == "domestic-return") return new("answer:30 days",[]);
        return new("ask-for-policy",[]);
    }

    private record EvalCase(string Id, string Family, string Split, string Slice, string Query,
        string[] ExpectedEvidence, string[] AllowedActions, string ExpectedOutcome);
    private record RagRow(string Id, string[] Ranking, string Citation, string Reference, string Answer, string Evidence);
    private record Trace(string Id, Step[] Steps);
    private record Step(int Order, string Tool, string OrderId, string Status, string Reason);
    private record Policy(string Id, string Scope, string Text, string Outcome);
    private record Decision(string Outcome, string[] Actions);
}




