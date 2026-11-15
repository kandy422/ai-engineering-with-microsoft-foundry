using System.Text.Json;

static partial class RetrievalLab
{
    static object Hosted()
    {
        var questions = LoadHosted("m09-embed-01");
        var policies = LoadHosted("m09-embed-02");
        var fixture = JsonSerializer.Deserialize<Questions>(File.ReadAllText("lab/questions.json"))!;
        var vector = questions[fixture.Query];
        var neighbors = fixture.Candidates.Select(item => new Hit(item.Id, item.Text,
            Similarity.Cosine(vector, questions[item.Text])))
            .OrderByDescending(hit => hit.Score).ThenBy(hit => hit.Id, StringComparer.Ordinal).ToArray();
        // This literal check is specific to the teaching fixture's deadline wording.
        var afterDeadline = neighbors.Where(hit =>
            !hit.Text.Contains("within 30 days", StringComparison.OrdinalIgnoreCase)).ToArray();
        return new {
            Embeddings = new { fixture.Query, QueryVector = vector, Dimensions = vector.Length, Neighbors = neighbors },
            AfterDeadline = afterDeadline,
            Hybrid = Hybrid(text => policies[text]),
            Filter = Filter(text => policies[text]),
            Evaluation = Evaluate(text => policies[text])
        };
    }

    static Dictionary<string, float[]> LoadHosted(string recording)
    {
        var folder = Path.Combine("lab", "hosted-evidence", recording);
        using var request = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "request.json")));
        using var response = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "response.json")));
        var input = request.RootElement.GetProperty("input").EnumerateArray().Select(item => item.GetString()!).ToArray();
        var vectors = new Dictionary<string, float[]>(StringComparer.Ordinal);
        var seen = new HashSet<int>();
        foreach (var row in response.RootElement.GetProperty("data").EnumerateArray())
        {
            var index = row.GetProperty("index").GetInt32();
            var vector = row.GetProperty("embedding").EnumerateArray().Select(value => value.GetSingle()).ToArray();
            if (index < 0 || index >= input.Length || !seen.Add(index) ||
                vector.Length != 1536 || !vector.All(float.IsFinite) || vector.All(value => value == 0))
                throw new InvalidDataException($"Invalid vector at index {index} in {recording}.");
            vectors.Add(input[index], vector);
        }
        if (seen.Count != input.Length)
            throw new InvalidDataException($"Missing vectors in {recording}.");
        return vectors;
    }
}
