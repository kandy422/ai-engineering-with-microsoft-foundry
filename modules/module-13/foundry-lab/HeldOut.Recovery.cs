using System.ClientModel;
using System.Security.Cryptography;
using System.Text.Json;
using Azure.AI.Extensions.OpenAI;
using Azure.AI.Projects;
using Azure.Identity;

#pragma warning disable OPENAI001

static partial class HeldOut
{
    private static async Task Recover(string root, Dictionary<string, string> files, string action)
    {
        var original = Path.Combine(root, "receipts", "course-20261115");
        var folder = Path.Combine(root, "receipts", "course-20261115-recovery");
        var config = Read(original, "configuration");
        var expectedEndpoint = RequiredString(config, "endpoint");
        var definition = Read(original, "evaluation-request");
        var grader = definition.GetProperty("testing_criteria").EnumerateArray()
            .Single(g => RequiredString(g, "name") == "policy-outcome-judge");
        if (RequiredString(grader, "model") != Judge ||
            grader.GetProperty("sampling_params").GetProperty("max_completions_tokens").GetInt32() != 1024)
            throw new InvalidDataException("Original grader differs from the allocated model or output limit.");

        var rows = new List<JsonElement>();
        var keys = new List<object>();
        foreach (var variant in Variants)
        {
            var sourceHash = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(files[variant])));
            if (sourceHash != RequiredString(config.GetProperty("manifest").GetProperty("datasets").GetProperty(variant), "sha256"))
                throw new InvalidDataException($"Original dataset hash differs for {variant}.");
            var originalRun = Read(original, $"{variant}-run");
            var inputs = File.ReadLines(files[variant]).Select(line => JsonDocument.Parse(line).RootElement.Clone())
                .ToDictionary(item => RequiredString(item, "caseId"));
            foreach (var output in Read(original, $"{variant}-items").EnumerateArray())
            {
                var prior = output.GetProperty("results").EnumerateArray()
                    .Single(g => RequiredString(g, "name") == "policy-outcome-judge");
                if (RequiredString(prior, "status") != "error")
                    continue;
                if (RequiredString(prior.GetProperty("sample").GetProperty("error"), "code") != "429" ||
                    RequiredString(output, "run_id") != RequiredString(originalRun, "id"))
                    throw new InvalidDataException("Unexpected original error or run.");
                var serviceInput = output.GetProperty("datasource_item");
                var caseId = RequiredString(serviceInput, "caseId");
                var input = inputs[caseId];
                foreach (var field in Fields)
                    if (RequiredString(serviceInput, field) != RequiredString(input, field))
                        throw new InvalidDataException($"Input mismatch for {variant}/{caseId}/{field}.");
                rows.Add(input);
                keys.Add(new { caseId, variant, version = RequiredString(input, "version"),
                    sourceDatasetSha256 = sourceHash,
                    rowSha256 = Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(input.GetRawText()))),
                    originalRunId = RequiredString(output, "run_id"), originalItemId = output.GetProperty("id"), originalJudge = prior });
            }
        }

        if (rows.Count != 7 || rows.Select(r => (RequiredString(r, "caseId"), RequiredString(r, "version"))).Distinct().Count() != 7)
            throw new InvalidDataException("Expected exactly seven distinct throttled evaluations.");
        var definitionBody = BinaryData.FromObjectAsJson(new { name = "module-13-judge-recovery",
            data_source_config = definition.GetProperty("data_source_config"), testing_criteria = new[] { grader } });
        var runBody = BinaryData.FromObjectAsJson(new
        {
            name = "module-13-judge-recovery-course-20261115",
            data_source = new { type = "jsonl", source = new { type = "file_content", content = rows.Select(item => new { item }) } }
        });
        if (action == "prepare")
        {
            Directory.CreateDirectory(folder);
            Save(folder, "subset-manifest", keys);
            File.WriteAllText(Path.Combine(folder, "evaluation-request.json"), definitionBody.ToString());
            File.WriteAllText(Path.Combine(folder, "run-request.json"), runBody.ToString());
            Console.WriteLine("Prepared seven throttled rows, one grader, no cloud calls.");
            return;
        }

        var endpoint = Environment.GetEnvironmentVariable("AZURE_AI_PROJECT_ENDPOINT");
        if (endpoint != expectedEndpoint)
            throw new InvalidOperationException("Use the original project endpoint.");
        var options = new AIProjectClientOptions { RetryPolicy = new System.ClientModel.Primitives.ClientRetryPolicy(0) };
        var project = new AIProjectClient(new Uri(expectedEndpoint), new AzureCliCredential(), options);
        var client = project.ProjectOpenAIClient.GetEvaluationClient();

        if (action == "submit")
        {
            using var claim = new FileStream(Path.Combine(folder, "submit.claim"), FileMode.CreateNew);
            Save(folder, "subset-manifest", keys);
            File.WriteAllText(Path.Combine(folder, "evaluation-request.json"), definitionBody.ToString());
            File.WriteAllText(Path.Combine(folder, "run-request.json"), runBody.ToString());
            using var evaluationContent = BinaryContent.Create(definitionBody);
            var evaluation = Capture(folder, "evaluation", await client.CreateEvaluationAsync(evaluationContent));
            using var runContent = BinaryContent.Create(runBody);
            var submission = Capture(folder, "submitted", await client.CreateEvaluationRunAsync(RequiredString(evaluation, "id"), runContent));
            Console.WriteLine($"Recovery run: {RequiredString(submission, "id")}");
            return;
        }

        var evalId = RequiredString(Read(folder, "evaluation"), "id");
        var runId = RequiredString(Read(folder, "submitted"), "id");
        var run = Capture(folder, "run", await client.GetEvaluationRunAsync(evalId, runId, options: new()));
        var status = RequiredString(run, "status");
        Console.WriteLine($"{runId} | {status}");
        if (status != "completed") return;
        var page = Capture(folder, "items-page", await client.GetEvaluationRunOutputItemsAsync(evalId, runId,
            limit: 12, order: "asc", after: null, outputItemStatus: null, options: new()));
        if (page.GetProperty("has_more").GetBoolean() || page.GetProperty("data").GetArrayLength() != 7)
            throw new InvalidDataException("Expected a single page of exactly seven recovery results.");
        Save(folder, "items", page.GetProperty("data"));
    }
}
