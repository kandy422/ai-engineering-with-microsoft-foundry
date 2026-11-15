using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;

static partial class RetrievalLab
{
    private static readonly JsonSerializerOptions OutputOptions = new() { WriteIndented = true };
    public static void Run(string command)
    {
        var result = command switch
        {
            "similarity" => Measure(),
            "chunk" => Chunk(), "azure-payloads" => AzurePayloads(),
            "hybrid" => Hybrid(),
            "filter" => Filter(),
            "evaluate" => Evaluate(),
            "retrieve" => Retrieve(),
            "hosted" => Hosted(),
            _ => throw new ArgumentException($"Unknown command: {command}")
        };
        Console.WriteLine(JsonSerializer.Serialize(result, OutputOptions));
    }

    static object Measure()
    {
        float[] query = [1, 0, 1];
        var examples = new[] { new { Id = "A", Vector = new float[] {1, 0, -1} },
            new { Id = "B", Vector = new float[] {1, 1, 1} },
            new { Id = "5 × B", Vector = new float[] {5, 5, 5} } };
        return new { Query = query, Results = examples.Select(item => new {
            item.Id, item.Vector,
            Dot = query.Zip(item.Vector).Sum(pair => pair.First * pair.Second),
            Cosine = Similarity.Cosine(query, item.Vector),
            ProjectedCosine = Similarity.Cosine(query[..2], item.Vector[..2])
        }).ToArray() };
    }

    static object Chunk()
    {
        const string sourceId = "returns-policy";
        const string version = "v1";
        var lines = File.ReadAllLines("lab/returns-policy.txt");
        var whole = string.Join(" ", lines);
        var words = whole.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        string[] Window(int width, int overlap) => Enumerable.Range(0, 1 + Math.Max(0, (words.Length - width + width - overlap - 1) / (width - overlap)))
            .Select(index => string.Join(" ", words.Skip(index * (width - overlap)).Take(width))).ToArray();

        var methods = new[] {
            new { Name = "10 words", Chunks = Window(10, 0) },
            new { Name = "10 words, 4 overlap", Chunks = Window(10, 4) },
            new { Name = "Section", Chunks = lines }
        };
        return new { SourceId = sourceId, Version = version, Source = lines, Methods = methods.Select(method => new {
            method.Name, Chunks = method.Chunks.Select((text, index) => new {
                Id = $"{sourceId}-{version}-{index + 1}", Text = text,
                CompleteException = text.Contains(lines[1], StringComparison.Ordinal)
            }).ToArray()
        }).ToArray() };
    }

    private static Document[] ReadDocuments() =>
        JsonSerializer.Deserialize<Document[]>(File.ReadAllText("lab/policy-index.json"))!;

    static object Hybrid()
    {
        using var encoder = new SentenceEncoder("lab/model");
        return Hybrid(encoder.Embed);
    }

    static object Hybrid(Func<string, float[]> embed)
    {
        var index = Index(ReadDocuments().Where(doc => doc.Scope == "public"), embed);
        var queries = new[] { "RTN-7041", "Can I send a notebook computer back after a month?", "return" };
        return queries.Select(query => {
            var keywords = KeywordOrder(index, query);
            var vectors = VectorOrder(index, embed(query), 3);
            var fused = Fuse(keywords, vectors.Select(hit => hit.Id).ToArray());
            return new { Query = query, Keyword = keywords, Vector = vectors, Hybrid = fused };
        }).ToArray();
    }

    static object Filter()
    {
        using var encoder = new SentenceEncoder("lab/model");
        return Filter(encoder.Embed);
    }

    static object Filter(Func<string, float[]> embed)
    {
        const string query = "Can I return a laptop with a recall after the standard deadline?";
        var index = Index(ReadDocuments(), embed);
        var vector = embed(query);
        var scoped = index.Where(item => item.Doc.Product == "laptop" && item.Doc.Scope == "public").ToArray();
        var before = VectorOrder(scoped, vector, 3);
        var eligible = scoped.ToDictionary(item => item.Doc.Id);
        var reranked = before.Select(hit => new {
            hit.Id,
            RecallMatch = eligible[hit.Id].Doc.Topic == "recall" && Words(query).Contains("recall"),
            hit.Score
        }).OrderByDescending(hit => hit.RecallMatch).ThenByDescending(hit => hit.Score).ToArray();
        return new { Query = query, Product = "laptop", AllowedScope = "public", TopK = 3,
            TrustedScopeExclusions = index.Where(item => item.Doc.Scope != "public").Select(item => item.Doc.Id).ToArray(),
            ProductExclusions = index.Where(item => item.Doc.Scope == "public" && item.Doc.Product != "laptop").Select(item => item.Doc.Id).ToArray(),
            Before = before, After = reranked };
    }

    static object Evaluate()
    {
        using var encoder = new SentenceEncoder("lab/model");
        return Evaluate(encoder.Embed);
    }

    static object Evaluate(Func<string, float[]> embed)
    {
        var documents = ReadDocuments().Where(doc => doc.Scope == "public").ToList();
        var queries = new[] {
            new { Text = "Can I send my notebook computer back after a month?", Relevant = new[] {"standard-v1", "recall-v1"} },
            new { Text = "My laptop was recalled. Can I return it?", Relevant = new[] {"recall-v1"} },
            new { Text = "How can I change my invoice address?", Relevant = new[] {"billing-v1"} }
        };
        var index = Index(documents, embed);
        const int k = 2;
        var rows = queries.Select(query => {
            var vector = embed(query.Text);
            var timer = Stopwatch.StartNew();
            var ids = VectorOrder(index, vector, k).Select(hit => hit.Id).ToArray();
            timer.Stop();
            return new { Query = query.Text, query.Relevant, Retrieved = ids,
                Recall = ids.Intersect(query.Relevant).Count() / (double)query.Relevant.Length,
                RankingMilliseconds = timer.Elapsed.TotalMilliseconds };
        }).ToArray();
        var old = documents.Single(doc => doc.Id == "standard-v1");
        var updated = old with { Id = "standard-v2", Content = "Laptop standard returns are accepted within 45 days of delivery." };
        var versionQuery = embed("How long do I have to return a laptop under the standard policy?");
        var before = VectorOrder(index, versionQuery, index.Length).First(hit => hit.Id == old.Id);
        documents.Add(updated);
        var withBoth = Index(documents, embed);
        var coexist = VectorOrder(withBoth, versionQuery, withBoth.Length)
            .Where(hit => hit.Id == old.Id || hit.Id == updated.Id).ToArray();
        documents.Remove(old);
        var updatedIndex = Index(documents, embed);
        var after = VectorOrder(updatedIndex, versionQuery, updatedIndex.Length).First(hit => hit.Id == updated.Id);
        if (updatedIndex.Any(item => item.Doc.Id == old.Id)) throw new InvalidOperationException("Obsolete chunk remains.");
        return new { K = k, Rows = rows, MeanRecall = rows.Average(row => row.Recall), Before = before,
            CoexistingVersions = coexist, After = after, Removed = old.Id };
    }

    static Indexed[] Index(IEnumerable<Document> documents, SentenceEncoder encoder) =>
        Index(documents, encoder.Embed);

    static Indexed[] Index(IEnumerable<Document> documents, Func<string, float[]> embed) =>
        documents.Select(doc => new Indexed(doc, embed(doc.Content))).ToArray();

    static string[] KeywordOrder(Indexed[] index, string query)
    {
        var queryWords = Words(query);
        return index.Select(item => new { item.Doc.Id, Count = Words(item.Doc.Content).Intersect(queryWords).Count() })
            .Where(item => item.Count > 0).OrderByDescending(item => item.Count)
            .ThenBy(item => item.Id, StringComparer.Ordinal).Take(3).Select(item => item.Id).ToArray();
    }

    static Hit[] VectorOrder(Indexed[] index, float[] vector, int k) =>
        index.Select(item => new Hit(item.Doc.Id, item.Doc.Content, Similarity.Cosine(item.Vector, vector)))
            .OrderByDescending(hit => hit.Score).ThenBy(hit => hit.Id, StringComparer.Ordinal).Take(k).ToArray();

    static object Fuse(string[] left, string[] right) =>
        left.Concat(right).Distinct(StringComparer.Ordinal).Select(id => new {
            Id = id,
            Score = ReciprocalRank(Array.IndexOf(left, id)) + ReciprocalRank(Array.IndexOf(right, id))
        }).OrderByDescending(hit => hit.Score).ThenBy(hit => hit.Id, StringComparer.Ordinal).ToArray();

    static double ReciprocalRank(int zeroBasedRank) => zeroBasedRank < 0 ? 0 : 1.0 / (60 + zeroBasedRank + 1);
    static string[] Words(string text) => TokenPattern().Matches(text.ToLowerInvariant()).Select(match => match.Value).ToArray();

    [GeneratedRegex("[a-z0-9]+(?:-[a-z0-9]+)*")]
    private static partial Regex TokenPattern();

    internal record Document(string Id, string Product, string Scope, string Topic, string Content);
    private record Indexed(Document Doc, float[] Vector);
    private record Hit(string Id, string Text, double Score);
}


