using System.Text.Json;

if (args is ["gate", var candidate])
{
    Environment.ExitCode = Checkpoints.Gate(candidate);
    return;
}

if (args is ["judge"])
{
    await LocalJudge.Run();
    return;
}

if (args.Length > 0 && args[0] != "13.1")
{
    Checkpoints.Run(args[0]);
    return;
}

var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
T Read<T>(string file) => JsonSerializer.Deserialize<T>(File.ReadAllText(file), options)
    ?? throw new InvalidDataException(file);

var rubric = Read<Rubric>("rubric.json");
var rows = Read<Observation[]>("baseline.json");
foreach (var row in rows)
{
    Console.WriteLine($"{row.Id}: route={row.CorrectRoute} support={row.Supported} safe={row.Safe}");
}

var routeRate = rows.Average(row => row.CorrectRoute ? 1m : 0m);
var supportRate = rows.Average(row => row.Supported ? 1m : 0m);
var unsafeCount = rows.Count(row => !row.Safe);
var rank = (int)Math.Ceiling(0.95m * rows.Length);
var p95 = rows.Select(row => row.Milliseconds).Order().ElementAt(rank - 1);
var meanCost = rows.Average(row => row.CostUsd);
var accepted = routeRate >= rubric.MinQualityRate
    && supportRate >= rubric.MinQualityRate
    && unsafeCount == 0
    && p95 <= rubric.MaxP95Milliseconds
    && meanCost <= rubric.MaxMeanCostUsd;
Console.WriteLine(FormattableString.Invariant($"Routing {routeRate:P0} | Support {supportRate:P0} | Unsafe {unsafeCount}"));
Console.WriteLine(FormattableString.Invariant($"P95 {p95} ms | Mean cost ${meanCost:F3}"));
Console.WriteLine(accepted ? "RELEASE ACCEPTED" : "RELEASE BLOCKED");

record Rubric(string Version, decimal MinQualityRate, int MaxP95Milliseconds, decimal MaxMeanCostUsd);
record Observation(string Id, string ExpectedRoute, string ActualRoute, bool Supported,
    string[] AllowedActions, string[] ActualActions, int Milliseconds, decimal CostUsd)
{
    public bool CorrectRoute => ExpectedRoute == ActualRoute;
    public bool Safe => ActualActions.All(AllowedActions.Contains);
}



