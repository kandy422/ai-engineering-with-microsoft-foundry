using System.Text.Json;

static class ValidateData
{
    public static void Run(string trainPath, string heldOutPath, JsonSerializerOptions options)
    {
        var labels = new Dictionary<string, int> { ["BILLING"] = 0, ["DELIVERY"] = 0, ["RETURNS"] = 0 };
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in File.ReadLines(trainPath))
        {
            using var row = JsonDocument.Parse(line);
            var messages = row.RootElement.GetProperty("messages").EnumerateArray().ToArray();
            var roles = messages.Select(message => message.GetProperty("role").GetString());
            if (!roles.SequenceEqual(new[] { "system", "user", "assistant" }))
                throw new InvalidDataException("Expected a system, user and assistant message.");
            var contents = messages.Select(message => message.GetProperty("content").GetString()?.Trim()).ToArray();
            if (contents.Any(string.IsNullOrWhiteSpace) || !seen.Add(contents[1]!) || !labels.ContainsKey(contents[2]!))
                throw new InvalidDataException("Empty, duplicated, or out-of-contract training row.");
            labels[contents[2]!]++;
        }

        using var test = JsonDocument.Parse(File.ReadAllText(heldOutPath));
        foreach (var row in test.RootElement.EnumerateArray())
        {
            var prompt = row.GetProperty("prompt").GetString()!;
            if (seen.Any(example => prompt.Contains(example, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("Training text leaked into held-out set.");
        }
        if (seen.Count < 10 || labels.Values.Any(count => count == 0))
            throw new InvalidDataException("Exercise minimum is ten rows covering all queues.");

        Console.WriteLine(JsonSerializer.Serialize(new
        {
            trainingRows = seen.Count, labels, heldOutRows = test.RootElement.GetArrayLength(),
            exactTextOverlap = 0, note = "Toy dataset. Check near duplicates and policy-specific training requirements separately."
        }, options));
    }
}
