using System.ClientModel;
using System.Text.Json;
using Azure.AI.Extensions.OpenAI;
using Azure.AI.Projects;
using Azure.Identity;
using OpenAI.Evals;

#pragma warning disable OPENAI001

if (args.Length > 0 && args[0] == "held-out")
{
    await HeldOut.Run(args[1..]);
    return;
}

var variants = new[] {"keyword", "scoped-keyword"};
var labDirectory = Path.GetFullPath("lab");
string FileFor(string variant) => Path.Combine(labDirectory, $"foundry-{variant}-v1.jsonl");
var datasetIds = new List<string[]>();
foreach (var variant in variants)
{
    var caseIds = new List<string>();
    foreach (var line in File.ReadLines(FileFor(variant)))
    {
        using var row = JsonDocument.Parse(line);
        foreach (var key in new[]{"caseId","query","response","reference","slice","version"})
        {
            if (row.RootElement.GetProperty(key).ValueKind != JsonValueKind.String)
                throw new InvalidDataException($"Invalid {key} in {variant}");
        }
        caseIds.Add(row.RootElement.GetProperty("caseId").GetString()!);
    }
    if (caseIds.Count == 0 || caseIds.Distinct().Count() != caseIds.Count)
        throw new InvalidDataException($"Empty dataset or repeated case IDs in {variant}.");
    datasetIds.Add(caseIds.Order().ToArray());
    Console.WriteLine($"{variant}: {caseIds.Count} valid rows");
}
if (!datasetIds[0].SequenceEqual(datasetIds[1]))
    throw new InvalidDataException("Variant case IDs differ.");

var schemaProperties = new[] {"caseId","query","response","reference","slice","version"}
    .ToDictionary(key => key, _ => new {type="string"});
var evaluation = new
{
    name = "module-13-retrieval-v1",
    data_source_config = new {
        type = "custom",
        item_schema = new {type="object", properties=schemaProperties, required=schemaProperties.Keys}
    },
    testing_criteria = new[] { new {
        type = "azure_ai_evaluator",
        name = "outcome-token-f1",
        evaluator_name = "builtin.f1_score",
        data_mapping = new {response="{{item.response}}",ground_truth="{{item.reference}}"}
    }}
};
if (args.Length == 0)
{
    Console.WriteLine("Case IDs match. Preflight only. Cloud submissions: 0.");
    Console.WriteLine(JsonSerializer.Serialize(evaluation, new JsonSerializerOptions {WriteIndented=true}));
    return;
}
if (args is not ["--submit", var datasetVersion] || string.IsNullOrWhiteSpace(datasetVersion))
    throw new ArgumentException("Use no arguments for local preflight or --submit DATASET_VERSION for an authorized project.");
var endpoint = Environment.GetEnvironmentVariable("AZURE_AI_PROJECT_ENDPOINT")
    ?? throw new InvalidOperationException("AZURE_AI_PROJECT_ENDPOINT is required.");
var project = new AIProjectClient(new Uri(endpoint), new DefaultAzureCredential());
EvaluationClient client = project.ProjectOpenAIClient.GetEvaluationClient();
using var evaluationContent = BinaryContent.Create(BinaryData.FromObjectAsJson(evaluation));
var definition = await client.CreateEvaluationAsync(evaluationContent);
var evaluationId = Id(definition);
Console.WriteLine($"Evaluation ID: {evaluationId}");
foreach (var variant in variants)
{
    var dataset = await project.Datasets.UploadFileAsync(
        name:$"module-13-{variant}", version:datasetVersion, filePath:FileFor(variant));
    using var runContent = BinaryContent.Create(BinaryData.FromObjectAsJson(new
    {
        name = $"module-13-{variant}-{datasetVersion}",
        data_source = new {type="jsonl",source=new {type="file_id",id=dataset.Value.Id}}
    }));
    var run = await client.CreateEvaluationRunAsync(evaluationId, runContent);
    Console.WriteLine($"{variant} | dataset={dataset.Value.Id} | run={Id(run)}");
}

static string Id(ClientResult result)
{
    using var doc = JsonDocument.Parse(result.GetRawResponse().Content.ToMemory());
    return doc.RootElement.GetProperty("id").GetString()
        ?? throw new InvalidDataException("Missing response ID.");
}


