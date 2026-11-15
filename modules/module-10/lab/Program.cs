using System.Text.Json;

if (args is ["--foundry-iq"])
{
    Console.WriteLine(JsonSerializer.Serialize(await FoundryIq.Compare(), new JsonSerializerOptions { WriteIndented = true }));
    return;
}

if (args.Length == 2 && args[0] == "--model")
{
    Console.WriteLine(JsonSerializer.Serialize(await LocalModel.Demo(args[1]), new JsonSerializerOptions { WriteIndented = true }));
    return;
}

if (args.Length > 0 && args[0] != "101")
{
    Console.WriteLine(JsonSerializer.Serialize(Pipeline.Demo(args[0]), new JsonSerializerOptions { WriteIndented = true }));
    return;
}

const string question = "What is the return window for an unopened accessory?";
var inputs = new[] {
    new Evidence("No passage", ""),
    new Evidence("Shipping policy", "Standard shipping takes five business days."),
    new Evidence("Returns policy", "Unopened accessories can be returned within 30 days.")
};
var comparison = inputs.Select(evidence => new {
    question,
    evidence,
    answer = AnswerFromEvidence(evidence.Text)
});
Console.WriteLine(JsonSerializer.Serialize(comparison, new JsonSerializerOptions { WriteIndented = true }));

static string AnswerFromEvidence(string passage) =>
    passage.Contains("Unopened accessories", StringComparison.OrdinalIgnoreCase)
        ? passage
        : "I cannot verify the return window.";

record Evidence(string Source, string Text);



