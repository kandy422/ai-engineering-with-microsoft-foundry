using System.Text.Json;

if (args.Length == 0)
{
    Console.Error.WriteLine("Pass a fixture JSON file, or use receipts, voice, integrate or vision.");
    Environment.ExitCode = 2;
    return;
}

switch (args[0])
{
    case "vision": await Vision.CompareAsync(args[1], args[2], args[3], args[4]); return;
    case "receipts": Checkpoints.ReceiptChecks(args[1]); return;
    case "voice": Checkpoints.VoiceChecks(args[1]); return;
    case "integrate": Checkpoints.IntegrationChecks(args[1], args[2], args.Length > 3 ? args[3] : null); return;
}

var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
var fixtures = JsonSerializer.Deserialize<Intake[]>(File.ReadAllText(args[0]), options)!;
foreach (var input in fixtures)
{
    var decision = Router.Route(input);
    Console.WriteLine($"{input.Name} | {decision.State} | {decision.Target}");
}

