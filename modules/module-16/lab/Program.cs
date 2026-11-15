var builder = WebApplication.CreateBuilder(args);
var mode = builder.Configuration["Capstone:Mode"] ?? "fixture";
if (mode == "hosted")
{
    await HostedCapstone.RunAsync(builder);
    return;
}
if (mode != "fixture") throw new InvalidOperationException("Capstone:Mode must be fixture or hosted.");
builder.WebHost.ConfigureKestrel(options => options.ListenLocalhost(8416));
var app = builder.Build();
var useLocalModel = app.Configuration.GetValue<bool>("UseLocalModel");
if (app.Configuration.GetValue<bool>("UseFoundryModel"))
    throw new InvalidOperationException("Use Capstone:Mode=hosted for Foundry.");
using var inference = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
{
    BaseAddress = new Uri("http://127.0.0.1:8516/v1/"),
    Timeout = TimeSpan.FromSeconds(20)
};
var modelMode = useLocalModel ? "local-qwen" : "local-fixture";
IKnowledgeModel model = useLocalModel
    ? new ChatKnowledgeModel(inference, "capstone-qwen")
    : app.Configuration.GetValue<bool>("ForceInvalidDraft") ? new InvalidCitationFixture() : new PassageCopyModel();
var desk = new SupportDesk(model, app.Configuration.GetValue<bool>("ForcePolicyUnavailable"));
using var starterOrders = new HttpClient { BaseAddress = new Uri("http://127.0.0.1:8401"), Timeout = TimeSpan.FromSeconds(3) };
var useStarterOrders = app.Configuration.GetValue<bool>("UseStarterOrders");
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapGet("/health", () => new { status = "ready", mode = modelMode });
app.MapPost("/answer", async (Question question, HttpContext context) =>
{
    var reply = await desk.AnswerAsync(question, context.RequestAborted);
    var traceId = System.Diagnostics.Activity.Current?.TraceId.ToString() ?? context.TraceIdentifier;
    context.Response.Headers["X-Correlation-Id"] = traceId;
    app.Logger.LogInformation("Policy case {TraceId} ended with {Outcome}", traceId, reply.Status);
    return reply;
});
app.MapPost("/order", async (Order request, CancellationToken cancelled) =>
{
    if (!useStarterOrders || request.Id == "O-204") return desk.Lookup(request);
    if (request.Customer != "customer-a") return new Reply("denied");
    if (request.Id is not { Length: 8 } || !request.Id.StartsWith("ORD-", StringComparison.Ordinal) ||
        !request.Id.Skip(4).All(char.IsAsciiDigit)) return new Reply("missing");
    try
    {
        using var response = await starterOrders.PostAsJsonAsync("/lookup", new { orderId = request.Id }, cancelled);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return new Reply("missing");
        if (!response.IsSuccessStatusCode) return new Reply("unavailable");
        var order = await response.Content.ReadFromJsonAsync<StarterOrder>(cancellationToken: cancelled);
        return order is { Status: "in_transit" } && order.OrderId == request.Id
            ? new Reply("found", order.Status)
            : new Reply("invalid_order");
    }
    catch (HttpRequestException) { return new Reply("unavailable"); }
    catch (OperationCanceledException) when (!cancelled.IsCancellationRequested) { return new Reply("unavailable"); }
    catch (System.Text.Json.JsonException) { return new Reply("invalid_order"); }
});
app.MapPost("/propose", (Change request) => desk.Propose(request));
app.MapPost("/approve", (Approval request) => desk.Approve(request));
app.MapPost("/attachment-check", (AttachmentMetadata request) => AttachmentPreflight.Check(request));
app.Run();

record Question(string? Topic, string Text);
record Order(string Customer, string Id);
record StarterOrder(string OrderId, string Status);
record Change(string Customer, string Ticket, string Action);
record Approval(string Customer, string Proposal);
record Reply(string Status, string? Value = null, string? Citation = null, int Writes = 0);

sealed class SupportDesk(IKnowledgeModel model, bool forcePolicyUnavailable)
{
    private readonly object gate = new();
    private readonly Dictionary<string, Change> proposals = new();
    private readonly Dictionary<string, string> applied = new();
    private readonly Policy[] policies =
    [
        ShippingPolicy.Load(Path.Combine(AppContext.BaseDirectory, "policies", "shipping.policy")),
        new("returns", "RET-30", "Unopened items can be returned within 30 days."),
        new("warranty", "WAR-A", "The warranty lasts one year."),
        new("warranty", "WAR-B", "The warranty lasts two years.")
    ];

    public async Task<Reply> AnswerAsync(Question question, CancellationToken cancelled)
    {
        if (string.IsNullOrWhiteSpace(question.Text)) return new("clarify");
        var topic = question.Topic ?? InferTopic(question.Text);
        if (forcePolicyUnavailable || topic == "outage") return new("unavailable");
        var evidence = policies.Where(p => p.Topic == topic).ToArray();
        if (evidence.Length == 0) return new("no_evidence");
        if (evidence.Length > 1) return new("conflicting_evidence");
        var source = evidence[0];
        try
        {
            var draft = await model.CompleteAsync(new ModelInput(
                "Copy the supporting passage verbatim and cite its ID.",
                question.Text, source), cancelled);
            return draft?.Citation == source.Id && draft.Value == source.Text
                ? new("answered", draft.Value, draft.Citation)
                : new("invalid_answer");
        }
        catch (HttpRequestException) { return new("unavailable"); }
        catch (OperationCanceledException) when (!cancelled.IsCancellationRequested) { return new("unavailable"); }
    }

    private static string? InferTopic(string text)
    {
        var returns = text.Contains("return", StringComparison.OrdinalIgnoreCase);
        var warranty = text.Contains("warranty", StringComparison.OrdinalIgnoreCase);
        var shipping = text.Contains("shipping", StringComparison.OrdinalIgnoreCase);
        var topics = (returns ? 1 : 0) + (warranty ? 1 : 0) + (shipping ? 1 : 0);
        if (topics != 1) return null;
        return returns ? "returns" : warranty ? "warranty" : "shipping";
    }

    public Reply Lookup(Order request)
    {
        if (request.Customer != "customer-a") return new("denied");
        return request.Id == "O-204" ? new("found", "Shipped") : new("missing");
    }

    public Reply Propose(Change request)
    {
        if (request.Customer != "customer-a" || request.Ticket != "T-17") return new("denied");
        if (request.Action != "Request callback") return new("unsupported_change");
        lock (gate)
        {
            var id = Guid.NewGuid().ToString("N");
            proposals.Add(id, request);
            return new("approval_required", id);
        }
    }

    public Reply Approve(Approval request)
    {
        lock (gate)
        {
            if (!proposals.TryGetValue(request.Proposal, out var change) ||
                change.Customer != request.Customer) return new("denied");
            var wrote = applied.TryAdd(request.Proposal, change.Action);
            return new(wrote ? "applied" : "already_applied", change.Action, Writes: wrote ? 1 : 0);
        }
    }
}

record Policy(string Topic, string Id, string Text);
record ModelInput(string Instruction, string Question, Policy Evidence);
record ModelDraft(string Value, string Citation);

interface IKnowledgeModel
{
    Task<ModelDraft?> CompleteAsync(ModelInput input, CancellationToken cancelled);
}

sealed class PassageCopyModel : IKnowledgeModel
{
    public Task<ModelDraft?> CompleteAsync(ModelInput input, CancellationToken cancelled) =>
        Task.FromResult<ModelDraft?>(new(input.Evidence.Text, input.Evidence.Id));
}

sealed class InvalidCitationFixture : IKnowledgeModel
{
    public Task<ModelDraft?> CompleteAsync(ModelInput input, CancellationToken cancelled) =>
        Task.FromResult<ModelDraft?>(new(input.Evidence.Text, "WRONG-REFERENCE"));
}
