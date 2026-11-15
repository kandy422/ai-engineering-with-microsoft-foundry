using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Security.Cryptography;
using LLama;
using LLama.Common;
using LLama.Sampling;
using Microsoft.ML.Tokenizers;

const string Order = "A-1042";
var lesson = args.FirstOrDefault() ?? "5.1";
object result = lesson switch
{
    "5.1" => InspectTokens(),
    "5.2" => await Decode(),
    "5.3" => Train(),
    "5.4" => await Context(),
    "5.5" => await Policies(),
    "5.6" => await Schedule(),
    "5.7" => Application(),
    "foundry-recording" when args.Length == 2 => FoundryRecording.Read(args[1]),
    "foundry-request" when args.Length == 2 => await FoundryRequest.SendAsync(args[1]),
    _ => throw new ArgumentException($"Unknown lesson {lesson}."),
};
Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));

static object InspectTokens()
{
    var tokenizer = TiktokenTokenizer.CreateForEncoding("cl100k_base");
    string[] inputs = ["Track order A-1042.", "return order.Id;", "追踪订单 A-1042。"];
    return inputs.Select(input =>
    {
        var tokens = tokenizer.EncodeToTokens(input, out _);
        var ids = tokens.Select(token => token.Id).ToArray();
        if (tokenizer.Decode(ids) != input)
            throw new InvalidOperationException("Token round trip failed.");
        return new { input, count = ids.Length, ids, pieces = tokens.Select(token => token.Value) };
    }).ToArray();
}

static double[] Softmax(double[] logits, double temperature)
{
    var exp = logits.Select(value => Math.Exp((value - logits.Max()) / temperature)).ToArray();
    return exp.Select(value => value / exp.Sum()).ToArray();
}

static async Task<object> Decode()
{
    var logits = new[] { 2.0, 1.0, 0.0 };
    var random = new Random(2026);
    var probabilities = Softmax(logits, 1);
    var draws = Enumerable.Range(0, 8).Select(_ =>
    {
        var draw = random.NextDouble();
        var selected = draw < probabilities[0] ? "check"
            : draw < probabilities[0] + probabilities[1] ? "find" : "ask";
        return new { draw, selected };
    }).ToArray();
    using var model = new LocalModel();
    const string prompt = "Give one short alternative to: I can check your order.";
    return new { candidates = new[] {"check", "find", "ask"}, logits, probabilities,
        cold = Softmax(logits, .5), draws, samples = new[]
        {
            await model.Ask(prompt, .3f, 24, 37),
            await model.Ask(prompt, .9f, 24, 38),
            await model.Ask(prompt, .9f, 24, 39)
        } };
}

static object Train()
{
    // One row of trainable logits for the prefix "To help, first".
    var weights = new double[2];
    var initial = weights.ToArray();
    foreach (var target in new[] { 0, 0, 0, 1 })
    {
        var probability = Softmax(weights, 1);
        for (var i = 0; i < weights.Length; i++)
            weights[i] += .5 * ((i == target ? 1 : 0) - probability[i]);
    }
    var pretrained = weights.ToArray();
    var supervised = Softmax(weights, 1);
    weights[0] -= .5 * supervised[0];
    weights[1] += .5 * supervised[0];
    var tuned = weights.ToArray();
    var preferenceGradient = 1 / (1 + Math.Exp(weights[1] - weights[0]));
    weights[0] -= .5 * preferenceGradient;
    weights[1] += .5 * preferenceGradient;
    var postTrained = weights.ToArray();
    var response = Softmax(weights, 1);
    var unchanged = postTrained.SequenceEqual(weights);
    if (!unchanged) throw new InvalidOperationException("Inference updated weights.");
    return new { candidates = new[] {"check the records", "ask for the order ID"},
        initial, pretrained, tuned, postTrained, inference = new { prefix = "To help, first",
        response, selected = response[1] > response[0] ? "ask for the order ID" : "check the records",
        weightsUnchanged = unchanged } };
}

static async Task<object> Context()
{
    using var model = new LocalModel();
    const string question = "Return the exact order ID shown above. If no ID is shown, say MISSING.";
    const string unrelated = "The customer asked about delivery.";
    var full = await model.Ask($"Order ID: {Order}\n{unrelated}\n{question}");
    var trimmed = await model.Ask($"{unrelated}\n{question}");
    var restored = await model.Ask($"Verified customer order ID: {Order}\n{question}");
    return new { full, trimmed, restored, expected = Order,
        fullPass = full.answer.Contains(Order), trimmedPass = trimmed.answer.Contains(Order),
        restoredPass = restored.answer.Contains(Order), appLookup = new { full = Extract($"Order ID: {Order}"), trimmed = Extract(unrelated), restored = Extract($"Verified customer order ID: {Order}") } };
    static string? Extract(string context)
    {
        var match = System.Text.RegularExpressions.Regex.Match(context, @"\b[A-Z]-\d{4}\b");
        return match.Success ? match.Value : null;
    }
}

static async Task<object> Policies()
{
    using var model = new LocalModel();
    const string policy = "Policy: Returns are accepted for 30 days from delivery. Final-sale items cannot be returned.";
    string[] questions = ["What is the standard return window?", "Can I return it?",
        "What is the international return shipping fee?"];
    var responses = new List<ModelRun>();
    foreach (var question in questions)
        responses.Add(await model.Ask($"{policy}\nAnswer briefly. {question}"));
    return new { policy, questions, responses,
        evidenceChecks = new[] {"30 days from delivery", "Item status and delivery date needed", "Fee absent from supplied policy"} };
}

static async Task<object> Schedule()
{
    using var model = new LocalModel();
    const string problem = "Assign Ava, Ben, and Chen to one slot each: 09, 10, 11. " +
        "Ava cannot do 09. Ben must be earlier than Chen. Chen cannot do 11. " +
        "Use this format only: Ava=HH, Ben=HH, Chen=HH.";
    var direct = await model.Ask(problem, .1f, 48);
    var hinted = await model.Ask("Ben and Chen must both be scheduled before 11. " + problem, .1f, 48);
    var candidates = from a in Enumerable.Range(9, 3)
                     from b in Enumerable.Range(9, 3)
                     from c in Enumerable.Range(9, 3)
                     where Valid(a, b, c)
                     select $"Ava={a:00}, Ben={b:00}, Chen={c:00}";
    return new { direct, directPass = Check(direct.answer), hinted, hintedPass = Check(hinted.answer),
        verified = candidates.Single() };
}

static bool Valid(int a, int b, int c) =>
    new[] { a, b, c }.All(slot => slot is >= 9 and <= 11) &&
    a != b && a != c && b != c && a != 9 && b < c && c != 11;

static bool Check(string answer)
{
    int Slot(string name) => int.TryParse(
        System.Text.RegularExpressions.Regex.Match(answer, $@"\b{name}\s*=\s*(\d{{2}})\b").Groups[1].Value,
        out var slot) ? slot : -1;
    return Valid(Slot("Ava"), Slot("Ben"), Slot("Chen"));
}

static object Application()
{
    var orders = new Dictionary<string, string> { [Order] = "Delayed", ["A-1043"] = "Delivered" };
    var request = new SupportRequest("Where is my order?", Order);
    LookupResult Lookup(string id) => new(id, orders[id]);
    var buggy = Lookup("A-1043");
    var corrected = Lookup(request.OrderId);
    return new { request, buggy, corrected, expected = orders[Order],
        badAnswer = RenderStatus(buggy), correctedAnswer = RenderStatus(corrected),
        badAccepted = AcceptResult(request, buggy), correctedAccepted = AcceptResult(request, corrected) };
}

static bool AcceptResult(SupportRequest request, LookupResult result) => request.OrderId == result.OrderId;
static string RenderStatus(LookupResult result) => $"Order {result.OrderId} is {result.Status.ToLowerInvariant()}.";

record SupportRequest(string Question, string OrderId);
record LookupResult(string OrderId, string Status);
record ModelRun(string prompt, string answer, float temperature, int maxOutput, uint seed, double elapsedMs,
    int inputTokens, int retokenizedAnswerTokens);

sealed class LocalModel : IDisposable
{
    private readonly LLamaWeights model;
    private readonly LLamaContext tokenizer;
    private readonly ModelParams parameters;

    public LocalModel()
    {
        parameters = new ModelParams(Path.GetFullPath("tools/smollm2.gguf"))
        {
            GpuLayerCount = 0, ContextSize = 1024, Threads = 3, BatchThreads = 3
        };
        model = LLamaWeights.LoadFromFile(parameters);
        tokenizer = model.CreateContext(parameters);
    }

    public async Task<ModelRun> Ask(string prompt, float temperature = 0, int maxOutput = 36, uint seed = 37)
    {
        var message = $"<|im_start|>user\n{prompt}<|im_end|>\n<|im_start|>assistant\n";
        var executor = new StatelessExecutor(model, parameters);
        var options = new InferenceParams
        {
            MaxTokens = maxOutput,
            SamplingPipeline = new DefaultSamplingPipeline { Temperature = temperature, Seed = seed }
        };
        var answer = new StringBuilder();
        var stopwatch = Stopwatch.StartNew();
        await foreach (var chunk in executor.InferAsync(message, options))
            answer.Append(chunk);
        stopwatch.Stop();
        var text = answer.ToString().Trim();
        return new ModelRun(prompt, text, temperature, maxOutput, seed, stopwatch.Elapsed.TotalMilliseconds,
            tokenizer.Tokenize(message, addBos: true, special: true).Length,
            tokenizer.Tokenize(text, addBos: false, special: false).Length);
    }

    public void Dispose()
    {
        tokenizer.Dispose();
        model.Dispose();
    }
}

