using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

static class Pipeline
{
    public record Passage(string Id, string Topic, DateOnly Effective, string Text, string File, string Hash, string[] Terms);
    public record Answer(string State, string Text, string[] Citations);
    public record Request(string Question, int WordBudget, Passage[] Context);
    private const string ReturnQuestion = "return accessory unopened";
    private static readonly HashSet<string> Stop = ["a", "an", "the", "what", "is", "can", "i", "it", "was", "if"];

    public static object Demo(string id)
    {
        var corpus = Load("lab/policies");
        return id switch
        {
            "102" => Ingest(),
            "103" => AnswerPipeline(corpus),
            "104" => Citations(corpus),
            "105" => Exceptions(corpus),
            "105-baseline" => Compose(Prepare(ReturnQuestion, corpus, 80)),
            "106" => Followup(corpus),
            "107" => Repair(corpus),
            "108" => new { question = ReturnQuestion, explicitPipeline = Compose(Prepare(ReturnQuestion, corpus, 40)) },
            _ => throw new ArgumentException("Unknown lesson")
        };
    }

    public static Passage[] Load(string directory) => Directory.EnumerateFiles(directory, "*.policy")
        .Order(StringComparer.Ordinal)
        .SelectMany(path => ReadPolicy(Path.GetFileName(path), File.ReadAllText(path))).ToArray();

    private static IEnumerable<Passage> ReadPolicy(string file, string contents)
    {
        var sections = Regex.Split(contents.Trim(), @"\r?\n\s*\r?\n");
        var metadata = sections[0].Split('|', StringSplitOptions.TrimEntries);
        foreach (var section in sections.Skip(1))
        {
            var parts = section.Split('|', 2, StringSplitOptions.TrimEntries);
            var text = Regex.Replace(parts[1], @"\s+", " ");
            var terms = Words(metadata[1] + " " + parts[0] + " " + text).Distinct().ToArray();
            yield return new Passage(metadata[0] + "#" + parts[0], metadata[1],
                DateOnly.ParseExact(metadata[2], "yyyy-MM-dd"), text, file,
                Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))),
                terms);
        }
    }

    private record IndexedPassage(Passage Passage, float[] Vector);

    private static object Upsert(Dictionary<string, IndexedPassage> index, Passage[] passages, SentenceEncoder encoder)
    {
        int changed = 0;
        foreach (var item in passages)
        {
            if (index.TryGetValue(item.Id, out var existing) && existing.Passage.Hash == item.Hash
                && existing.Passage.Effective == item.Effective && existing.Passage.Topic == item.Topic && existing.Passage.File == item.File)
                continue;
            var vector = existing?.Passage.Hash == item.Hash ? existing.Vector : encoder.Embed(item.Text);
            index[item.Id] = new IndexedPassage(item, vector);
            changed++;
        }
        var current = passages.Select(item => item.Id).ToHashSet();
        var removed = index.Keys.Where(key => !current.Contains(key)).ToArray();
        foreach (var key in removed)
            index.Remove(key);
        return new { changed, removed = removed.Length, total = index.Count };
    }

    private static object Ingest()
    {
        var modelDirectory = Environment.GetEnvironmentVariable("RAG_EMBEDDING_DIR") ?? (Directory.Exists("lab/model") ? "lab/model" : "../module-09/lab/model");
        using var encoder = new SentenceEncoder(modelDirectory);
        var index = new Dictionary<string, IndexedPassage>();
        var initial = Load("lab/policies");
        var first = Upsert(index, initial, encoder);
        var repeat = Upsert(index, initial, encoder);
        var updated = initial.Where(p => p.File != "returns.policy")
            .Concat(ReadPolicy("returns.policy", File.ReadAllText("lab/fixtures/returns-updated.policy"))).ToArray();
        var change = Upsert(index, updated, encoder);
        var revised = index["returns#unopened"];
        return new { first, repeat, change, updated = revised.Passage, embedding = new {
            model = "sentence-transformers/all-MiniLM-L6-v2",
            revision = File.ReadAllText(Path.Combine(modelDirectory, "revision.txt")).Trim(),
            dimensions = revised.Vector.Length,
            preview = revised.Vector.Take(5).Select(value => Math.Round(value, 4)).ToArray(),
            vector = revised.Vector
        }};
    }

    private static IEnumerable<string> Words(string text) => Regex.Matches(text.ToLowerInvariant(), @"[a-z0-9]+")
        .Select(m => m.Value).Where(word => !Stop.Contains(word));

    private static Passage[] Retrieve(string query, Passage[] corpus)
    {
        var terms = Words(query).ToHashSet();
        return corpus.Select(p => (Passage: p, Score: p.Terms.Count(terms.Contains)))
            .Where(hit => hit.Score > 0).OrderByDescending(hit => hit.Score)
            .ThenBy(hit => hit.Passage.Id, StringComparer.Ordinal)
            .Select(hit => hit.Passage).ToArray();
    }

    public static Request Prepare(string question, Passage[] corpus, int budget)
    {
        var context = new List<Passage>();
        int remaining = budget;
        foreach (var passage in Retrieve(question, corpus))
        {
            int cost = Regex.Matches(passage.Id + " " + passage.Text, @"\S+").Count;
            if (cost > remaining) continue;
            remaining -= cost;
            context.Add(passage);
        }
        return new Request(question, budget, context.ToArray());
    }

    private static Answer Compose(Request request)
    {
        var condition = Words(request.Question).FirstOrDefault(term => term is "opened" or "unopened");
        var relevant = request.Context.Where(p => p.Topic == "accessory returns").ToArray();
        if (condition is null && relevant.Length > 0)
            return new Answer("clarify", "Was the accessory opened?", []);
        var matching = relevant.Where(p => p.Id.EndsWith("#" + condition, StringComparison.Ordinal)).ToArray();
        if (matching.Length == 0)
            return new Answer("abstain", "I cannot verify the requested policy.", []);
        var latest = matching.Max(p => p.Effective);
        var active = matching.Where(p => p.Effective == latest).ToArray();
        if (active.Select(p => p.Text).Distinct().Count() > 1)
            return new Answer("conflict", "Policy passages disagree. Escalate for review.", active.Select(p => p.Id).ToArray());
        return new Answer("supported", active[0].Text, [active[0].Id]);
    }

    private static object AnswerPipeline(Passage[] corpus)
    {
        var request = Prepare(ReturnQuestion, corpus, 24);
        var payload = new { request.Question, Evidence = request.Context.Select(p => new { p.Id, p.Text }) };
        var squeezed = Prepare(ReturnQuestion, corpus, 5);
        return new { request.WordBudget, payload, result = Compose(request), squeezed = Compose(squeezed) };
    }

    private static object Citations(Passage[] corpus)
    {
        var answer = Compose(Prepare(ReturnQuestion, corpus, 40));
        var wrong = corpus.Single(p => p.Id == "shipping#standard");
        var correct = corpus.Single(p => p.Id == answer.Citations.Single());
        var page = new StringBuilder("<html><head><meta charset=\"utf-8\"/><title>Policy citation lab</title></head><body>");
        foreach (var passage in new[] { wrong, correct })
        {
            page.Append($"<p>{answer.Text} <a href=\"#{passage.Id.Replace('#', '-')}\">{passage.Id}</a></p>");
        }
        foreach (var passage in new[] { wrong, correct })
            page.Append($"<p id=\"{passage.Id.Replace('#', '-')}\">{passage.Text}</p>");
        Directory.CreateDirectory("lab/checkpoints");
        page.Append("</body></html>");
        File.WriteAllText("lab/checkpoints/citations.html", "<!doctype html>" + page);
        var document = XDocument.Parse(page.ToString());
        return new { claim = answer.Text, wrong = Check(wrong), correct = Check(correct) };
        object Check(Passage passage)
        {
            var link = document.Descendants("a").Single(node => node.Value == passage.Id);
            var fragment = ((string?)link.Attribute("href"))?.TrimStart('#');
            var target = document.Descendants("p").SingleOrDefault(node => (string?)node.Attribute("id") == fragment
                && node.Attribute("id") is not null);
            return new { passage.Id, passage.Text, resolves = target is not null,
                exactPolicyText = target?.Value == answer.Text };
        }
    }

    private static object Exceptions(Passage[] corpus)
    {
        var contradictory = ReadPolicy("regional.policy", File.ReadAllText("lab/fixtures/conflicting.policy")).Single();
        return new {
            missing = Compose(Prepare("gift cards expiration", corpus, 80)),
            ambiguous = Compose(Prepare("return accessory", corpus, 80)),
            conflict = Compose(Prepare(ReturnQuestion, [..corpus, contradictory], 80)),
            conflictDate = contradictory.Effective
        };
    }

    private static object Followup(Passage[] corpus)
    {
        const string history = "What is the accessory return window?";
        const string followup = "What if it was opened?";
        var raw = Prepare(followup, corpus, 40);
        var subject = Words(history).First(word => word == "accessory");
        var condition = Words(followup).Single(word => word == "opened");
        var rewritten = Prepare($"{subject} return {condition}", corpus, 40);
        return new { history, followup, rawIds = raw.Context.Select(p => p.Id), rewritten.Question,
            retrieved = rewritten.Context.Select(p => new { p.Id, p.Text }), answer = Compose(rewritten) };
    }

    private static object Repair(Passage[] corpus)
    {
        string[] questions = ["sealed add-on", "accessory return opened", "gift card expiration"];
        string[] expected = ["supported", "supported", "abstain"];
        string Expand(string question) => question == questions[0] ? question + " accessory return unopened" : question;
        var runs = questions.Select((question, i) => new {
            question, expanded = Expand(question),
            beforeIds = Retrieve(question, corpus).Select(p => p.Id).ToArray(),
            afterIds = Retrieve(Expand(question), corpus).Select(p => p.Id).ToArray(),
            before = Compose(Prepare(question, corpus, 40)),
            after = Compose(Prepare(Expand(question), corpus, 40)),
            expected = expected[i]
        }).ToArray();
        if (runs.Any(run => run.after.State != run.expected)
            || runs[0].beforeIds.Length != 0 || !runs[0].after.Citations.SequenceEqual(["returns#unopened"])
            || !runs[1].before.Citations.SequenceEqual(runs[1].after.Citations)) throw new InvalidOperationException("Regression");
        return runs;
    }
}







