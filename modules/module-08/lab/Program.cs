using System.Text.Json;

var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true };
#if !LEARNER_PACKAGE
if (args[0] == "benchmark-foundry-plan") { FoundryBenchmark.WritePlan(args[1], args[2], options); return; }
if (args[0] == "benchmark-foundry-run") { await FoundryBenchmark.Run(args[1..], options); return; }
#endif
if (args[0] == "benchmark") { await Benchmark.Run(args[1..], options); return; }
if (args[0] == "validate") { ValidateData.Run(args[1], args[2], options); return; }
if (args[0] == "smoke") { await Routing.Smoke(args[1..], options); return; }
var matrix = JsonSerializer.Deserialize<Matrix>(File.ReadAllText(args[0]), options)
    ?? throw new InvalidDataException("Missing matrix.");
var rows = from task in matrix.Tasks
           from model in matrix.Candidates
           let gates = new[]
           {
               (Pass: model.Modalities.Contains(task.Modality), Name: "modality"),
               (Pass: model.Context >= task.Tokens, Name: "context"),
               (Pass: task.Hosting.Contains(model.Hosting), Name: "hosting")
           }
           let failures = gates.Where(gate => !gate.Pass).Select(gate => gate.Name).ToArray()
           select new { task = task.Name, model = model.Name, eligible = failures.Length == 0, failures };
Console.WriteLine(JsonSerializer.Serialize(rows, options));

record Matrix(Requirement[] Tasks, Candidate[] Candidates);
record Requirement(string Name, string Modality, int Tokens, string[] Hosting);
record Candidate(string Name, string[] Modalities, int Context, string Hosting);



