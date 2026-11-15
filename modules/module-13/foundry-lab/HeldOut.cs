using System.ClientModel;
using System.Security.Cryptography;
using System.Text.Json;
using Azure.AI.Extensions.OpenAI;
using Azure.AI.Projects;
using Azure.Identity;

#pragma warning disable OPENAI001

static partial class HeldOut
{
    private static readonly string[] Variants = ["keyword", "scoped-keyword"];
    private static readonly string[] Fields = ["caseId", "query", "slice", "version", "response", "reference", "context"];
    private static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true };
    private const string Judge = "course-chat";

    public static async Task Run(string[] args)
    {
        var root = Path.GetFullPath("remediation-13.6");
        var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "prepared-inputs.json"))).RootElement.Clone();
        var frozenReference = File.ReadAllText(Path.Combine(root, "held-out-v1.sha256")).Trim();
        if (RequiredString(manifest, "referenceSha256") != frozenReference ||
            Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(root, "held-out-v1.json")))) != frozenReference)
            throw new InvalidDataException("The frozen reference file changed.");
        var files = Variants.ToDictionary(variant => variant, variant => ValidateDataset(root, manifest, variant));
        if (args is ["recover", var recoveryAction] && recoveryAction is "prepare" or "submit" or "collect")
        {
            await Recover(root, files, recoveryAction);
            return;
        }
        var definitions = Fields.ToDictionary(field => field, _ => new { type = "string" });
        var request = BinaryData.FromObjectAsJson(new
        {
            name = "module-13-held-out-v1",
            data_source_config = new
            {
                type = "custom",
                item_schema = new { type = "object", properties = definitions, required = Fields }
            },
            testing_criteria = new object[]
            {
                new { type = "string_check", name = "exact-outcome", input = "{{item.response}}",
                      reference = "{{item.reference}}", operation = "eq" },
                new
                {
                    type = "score_model", name = "policy-outcome-judge", model = Judge,
                    pass_threshold = 1, range = new[] { 0, 1 },
                    sampling_params = new { max_completions_tokens = 1024, reasoning_effort = "minimal" },
                    input = new[]
                    {
                        new { role = "system", content = "Score this fictional support decision. Give 1 only if the candidate selects the same policy outcome as the reference, preserving any stated time limit. With missing evidence, asking for the policy is correct when the reference asks for it. Otherwise give 0. Return only the score." },
                        new { role = "user", content = "Question: {{item.query}}\nRetrieved policy: {{item.context}}\nCandidate: {{item.response}}\nReference outcome: {{item.reference}}" }
                    }
                }
            }
        });
        if (args.Length == 0)
        {
            Console.WriteLine(request);
            return;
        }

        if (args.Length != 2 || args[0] is not ("submit" or "collect") ||
            !System.Text.RegularExpressions.Regex.IsMatch(args[1], @"^[a-z0-9-]{1,32}$"))
            throw new ArgumentException("Expected held-out submit|collect BATCH_ID.");
        string mode = args[0], batch = args[1];
        var endpoint = Environment.GetEnvironmentVariable("AZURE_AI_PROJECT_ENDPOINT")
            ?? throw new InvalidOperationException("Set the allocated Foundry project endpoint.");
        var uri = new Uri(endpoint);
        if (uri.Scheme != Uri.UriSchemeHttps || !uri.Host.EndsWith(".services.ai.azure.com", StringComparison.OrdinalIgnoreCase)
            || !uri.AbsolutePath.StartsWith("/api/projects/", StringComparison.Ordinal) ||
            uri.Query.Length > 0 || uri.Fragment.Length > 0 || uri.UserInfo.Length > 0)
            throw new InvalidOperationException("Expected an HTTPS Foundry project endpoint.");
        var folder = Path.Combine(root, "receipts", batch);
        if (mode == "submit")
        {
            Directory.CreateDirectory(folder);
            using var claim = new FileStream(Path.Combine(folder, "submit.claim"), FileMode.CreateNew);
            Save(folder, "configuration", new { endpoint, judge = Judge, batch, manifest });
            File.WriteAllText(Path.Combine(folder, "evaluation-request.json"), request.ToString());
        }
        else
        {
            using var config = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "configuration.json")));
            if (RequiredString(config.RootElement, "endpoint") != endpoint ||
                Variants.Any(variant => RequiredString(config.RootElement.GetProperty("manifest").GetProperty("datasets").GetProperty(variant), "sha256") !=
                    RequiredString(manifest.GetProperty("datasets").GetProperty(variant), "sha256")))
                throw new InvalidDataException("The collection endpoint or dataset hashes differ from the submission configuration.");
        }

        var options = new AIProjectClientOptions { RetryPolicy = new System.ClientModel.Primitives.ClientRetryPolicy(0) };
        var project = new AIProjectClient(uri, new AzureCliCredential(), options);
        var client = project.ProjectOpenAIClient.GetEvaluationClient();
        if (mode == "submit")
        {
            using var body = BinaryContent.Create(request);
            var evaluation = Capture(folder, "evaluation", await client.CreateEvaluationAsync(body));
            foreach (var variant in Variants)
            {
                var dataset = await project.Datasets.UploadFileAsync(
                    name: $"module-13-held-out-{variant}", version: batch, filePath: files[variant]);
                Capture(folder, $"{variant}-dataset", dataset);
                using var runBody = BinaryContent.Create(BinaryData.FromObjectAsJson(new
                {
                    name = $"module-13-{variant}-{batch}",
                    data_source = new { type = "jsonl", source = new { type = "file_id", id = dataset.Value.Id } }
                }));
                var run = Capture(folder, $"{variant}-submitted", await client.CreateEvaluationRunAsync(
                    RequiredString(evaluation, "id"), runBody));
                Console.WriteLine($"{variant} | dataset={dataset.Value.Id} | run={RequiredString(run, "id")}");
            }
            return;
        }

        var evaluationId = RequiredString(Read(folder, "evaluation"), "id");
        foreach (var variant in Variants)
        {
            var runId = RequiredString(Read(folder, $"{variant}-submitted"), "id");
            var run = Capture(folder, $"{variant}-run", await client.GetEvaluationRunAsync(evaluationId, runId, options: new()));
            var status = RequiredString(run, "status");
            Console.WriteLine($"{variant} | {runId} | {status}");
            if (status != "completed")
                continue;

            var items = new List<JsonElement>();
            string? after = null;
            for (int page = 1; page <= 12; page++)
            {
                var output = Capture(folder, $"{variant}-page-{page:00}",
                    await client.GetEvaluationRunOutputItemsAsync(evaluationId, runId, limit: 12,
                        order: "asc", after: after, outputItemStatus: null, options: new()));
                items.AddRange(output.GetProperty("data").EnumerateArray().Select(item => item.Clone()));
                if (!output.GetProperty("has_more").GetBoolean())
                    break;
                after = RequiredString(output, "last_id");
                if (page == 12) throw new InvalidDataException("Output pagination did not terminate.");
            }

            Save(folder, $"{variant}-items", items);
            var expected = File.ReadLines(files[variant]).Select(line =>
            {
                using var row = JsonDocument.Parse(line);
                return RequiredString(row.RootElement, "caseId");
            });
            var actual = items.Select(item => RequiredString(item.GetProperty("datasource_item"), "caseId"));
            if (!expected.Order().SequenceEqual(actual.Order()))
                throw new InvalidDataException($"The results for {variant} do not match the submitted case IDs.");
            Console.WriteLine($"{variant} | {items.Count} verified items");
        }
    }

    private static string ValidateDataset(string root, JsonElement manifest, string variant)
    {
        var expected = manifest.GetProperty("datasets").GetProperty(variant);
        var file = Path.Combine(root, "work", $"foundry-{variant}-v1.jsonl");
        var bytes = File.ReadAllBytes(file);
        if (!Convert.ToHexStringLower(SHA256.HashData(bytes)).Equals(RequiredString(expected, "sha256"), StringComparison.Ordinal))
            throw new InvalidDataException($"Prepared rows changed for {variant}.");
        if (File.ReadLines(file).Count() != 12 || File.ReadLines(file).Any(line => line.Length > 2048))
            throw new InvalidDataException($"Expected exactly 12 bounded rows for {variant}.");
        return file;
    }

    private static JsonElement Capture(string folder, string name, ClientResult result)
    {
        var response = result.GetRawResponse();
        var headers = new Dictionary<string, string>();
        foreach (var key in new[] { "x-request-id", "request-id", "apim-request-id", "x-ms-request-id" })
            if (response.Headers.TryGetValue(key, out var value) && value is not null) headers[key] = value;
        Save(folder, $"{name}-receipt", new { status = response.Status, utc = DateTimeOffset.UtcNow, headers });
        File.WriteAllText(Path.Combine(folder, name + ".json"), response.Content.ToString());
        return Read(folder, name);
    }

    private static JsonElement Read(string folder, string name)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, name + ".json")));
        return doc.RootElement.Clone();
    }

    private static void Save(string folder, string name, object value) =>
        File.WriteAllText(Path.Combine(folder, name + ".json"), JsonSerializer.Serialize(value, Pretty));

    private static string RequiredString(JsonElement obj, string key) =>
        obj.GetProperty(key).GetString() ?? throw new InvalidDataException($"Missing {key}.");
}


