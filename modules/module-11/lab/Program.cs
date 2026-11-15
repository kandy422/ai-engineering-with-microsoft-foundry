using System.Text.Json;
using static Orders;
using System.Text.RegularExpressions;

if (args is ["mcp-server"])
{
    await McpLab.ServeAsync();
    return;
}
var lesson = args.SingleOrDefault() ?? "11.1";
var customer = new Caller("alex");
const string orderId = "ORD-1042";
const string prompt = "Where is order ORD-1042?";
const string ToolSchema = """
{
  "name": "lookup_order",
  "description": "Get current status of an order visible to the caller.",
  "inputSchema": {
    "type": "object",
    "properties": {"orderId":{"type":"string","pattern":"^ORD-[0-9]{4}$"}},
    "required": ["orderId"],
    "additionalProperties": false
  }
}
""";
switch (lesson)
{
    case "11.1":
        Log("input", new { prompt, customer.Id });
        Log("text-only", "I need a source of order data.");
        var fixedResult = Lookup(customer, new(orderId));
        Require(fixedResult.Status == "Shipped");
        Log("workflow", fixedResult);
        var candidate = DecisionStub(prompt);
        Log("stub-proposal", candidate);
        var agentResult = Dispatch(customer, candidate);
        Require(agentResult == fixedResult);
        Log("agent-tool-result", agentResult);
        var unsupported = Dispatch(customer, new("cancel_order", orderId));
        Require(unsupported.Code == "unknown_tool");
        Log("blocked-proposal", unsupported);
        break;
    case "11.2":
        using (var schema = JsonDocument.Parse(ToolSchema))
            Log("schema", schema.RootElement);
        foreach (var (label, json, expected) in new[]
        {
            ("valid", """{"orderId":"ORD-1042"}""", "ok"),
            ("malformed", """{"orderId":1042}""", "invalid_argument"),
            ("inaccessible", """{"orderId":"ORD-2045"}""", "not_available"),
            ("spoof", """{"orderId":"ORD-2045","caller":"sam"}""", "invalid_argument")
        })
        {
            var result = Orders.Invoke(customer, json);
            Require(result.Code == expected);
            Log(label, new { arguments = JsonSerializer.Deserialize<JsonElement>(json), result });
        }
        break;
    case "11.3":
        int invocations = 0;
        Log("request", new { prompt, tools = new[] { "lookup_order" } });
        Log("text", new { text = "I will look up ORD-1042.", invocations });
        var call = new { callId = "call-001", proposal = DecisionStub(prompt) };
        Log("structured-call", call);
        var toolResult = Dispatch(customer, call.proposal);
        invocations++;
        Log("tool-output", new { call.callId, toolResult, invocations });
        Require(toolResult.Status == "Shipped" && invocations == 1);
        Log("reply", new { text = $"Order {orderId} is {toolResult.Status}.", source = call.callId });
        break;
    case "11.5":
        var memoryPath = Path.Combine(AppContext.BaseDirectory, $"session-{Guid.NewGuid():N}.json");
        var store = new SessionStore(memoryPath);
        var key = "session-1";
        store.Save(customer, key, new(orderId, DateTimeOffset.UnixEpoch.AddMinutes(10)));
        var now = DateTimeOffset.UnixEpoch.AddMinutes(1);
        store = new SessionStore(memoryPath);
        var resumed = store.Resume(customer, key, now);
        Require(resumed == orderId);
        Log("resumed", new { caller = customer.Id, key, rememberedOrderId = resumed });
        var other = store.Resume(new("sam"), key, now);
        Require(other is null);
        Log("other", new { caller = "sam", key, rememberedOrderId = other });
        var expired = store.Resume(customer, key, now.AddMinutes(10));
        Require(expired is null);
        Log("expired", new { caller = customer.Id, rememberedOrderId = expired });
        store.Save(customer, key, new(orderId, now.AddMinutes(10)));
        store.Forget(customer, key);
        var forgotten = store.Resume(customer, key, now);
        Require(forgotten is null);
        Log("forgotten", new { caller = customer.Id, rememberedOrderId = forgotten });
        File.Delete(memoryPath);
        break;
    case "11.6":
        using (var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(2)))
        {
            var tickets = new TicketStore();
            var attempts = 0;
            string Create()
            {
                var id = tickets.Create(customer, "delivery-1042");
                if (++attempts == 1) throw new TimeoutException("Injected response lost after commit.");
                return id;
            }
            string? ticket = null;
            for (var attempt = 1; attempt <= 2; attempt++)
            {
                deadline.Token.ThrowIfCancellationRequested();
                try { ticket = Create(); break; }
                catch (TimeoutException) when (attempt == 1)
                {
                    Log("lost-response", new { attempt, committedTickets = tickets.Count });
                }
            }
            Require(ticket == "T-1" && tickets.Count == 1 && attempts == 2);
            Log("retry", new { attempts, ticket, committedTickets = tickets.Count });
            for (var step = 1; step <= 3; step++)
            {
                deadline.Token.ThrowIfCancellationRequested();
                Log("loop-step", new { step, result = Dispatch(customer, DecisionStub(prompt)).Code });
            }
            Log("stop", new { reason = "step_limit", steps = 3, outcome = "escalate" });
        }
        break;
    case "11.7":
        var gate = new ApprovalGate();
        var change = new TicketChange("T-1", "Open", "Awaiting shipment");
        gate.Preview("approval-1", customer, change);
        Log("preview", new { approvalId = "approval-1", change });
        var denied = gate.Apply("approval-1", new("sam"), change);
        Require(denied == "denied" && gate.Audit.Count == 0);
        Log("denied", new { caller = "sam", result = denied });
        var altered = gate.Apply("approval-1", customer, change with { After = "Closed" });
        Require(altered == "denied" && gate.Audit.Count == 0);
        Log("altered", new { requestedAfter = "Closed", result = altered });
        var pending = gate.Apply("approval-1", customer, change);
        Require(pending == "approval_required" && gate.Audit.Count == 0);
        Log("pending", new { result = pending });
        Require(gate.Confirm("approval-1", customer, change));
        var applied = gate.Apply("approval-1", customer, change);
        var replay = gate.Apply("approval-1", customer, change);
        Require(applied == "applied" && replay == "already_applied" && gate.Audit.Count == 1);
        Log("applied", gate.Audit.Single());
        Log("replay", new { result = replay, auditRecords = gate.Audit.Count });
        break;
    case "live-tool-loop":
        using (var model = new LocalModel(ToolSchema, Orders.Invoke))
        {
            var live = await model.OrderAsync(customer, prompt, Log);
            Require(live.Result.Status == "Shipped" &&
                live.Answer.Contains("Shipped", StringComparison.OrdinalIgnoreCase));
        }
        break;
    case "live-orchestration":
        using (var model = new LocalModel(ToolSchema, Orders.Invoke))
        {
            var liveCases = new[]
            {
                (Caller: customer, Order: "ORD-1042", Expected: "Shipped"),
                (Caller: new Caller("sam"), Order: "ORD-2045", Expected: "Processing"),
                (Caller: customer, Order: "ORD-2045", Expected: "Escalate"),
                (Caller: customer, Order: "ORD-9999", Expected: "Escalate")
            };
            foreach (var split in new[] { false, true })
            {
                int correct = 0;
                double duration = 0;
                int modelCalls = 0;
                int lookupCalls = 0;
                foreach (var scenario in liveCases)
                {
                    var run = await model.OrderAsync(scenario.Caller,
                        $"Where is order {scenario.Order}?", Log, split, labelsOnly: true);
                    var answerMatches = string.Equals(run.Answer.TrimEnd('.'), scenario.Expected, StringComparison.OrdinalIgnoreCase);
                    var statusMatches = (run.Result.Code == "not_available" ? "Escalate" : run.Result.Status) == scenario.Expected;
                    if (answerMatches && statusMatches) correct++;
                    duration += run.ElapsedMilliseconds;
                    modelCalls += run.ModelCalls;
                    lookupCalls += run.LookupCalls;
                }
                Log(split ? "live-split" : "live-single", new
                {
                    correct,
                    cases = liveCases.Length,
                    modelCalls,
                    lookupCalls,
                    totalMilliseconds = Math.Round(duration, 1)
                });
            }
        }
        break;
    case "11.8":
        await McpLab.RunAsync(Log);
        break;
    case "11.9":
        var cases = new[]
        {
            (Caller: customer, Order: "ORD-1042", Expected: "Shipped"),
            (Caller: new Caller("sam"), Order: "ORD-2045", Expected: "Processing"),
            (Caller: customer, Order: "ORD-2045", Expected: "Escalate"),
            (Caller: customer, Order: "ORD-9999", Expected: "Escalate")
        };
        Log("cases", cases.Select(c => new { c.Caller.Id, c.Order, c.Expected }));
        foreach (var split in new[] { false, true })
        {
            var correct = 0;
            var decisions = 0;
            var lookups = 0;
            var watch = System.Diagnostics.Stopwatch.StartNew();
            foreach (var scenario in cases)
            {
                var proposed = DecisionStub($"Where is order {scenario.Order}?");
                decisions++;
                var result = Dispatch(scenario.Caller, proposed);
                lookups++;
                if (split && result.Status is null) decisions++; // Escalation specialist stub.
                if ((result.Status ?? "Escalate") == scenario.Expected) correct++;
            }
            watch.Stop();
            Require(correct == cases.Length);
            Log(split ? "split" : "single", new
            {
                correct,
                cases = cases.Length,
                decisions,
                lookups,
                elapsedMicroseconds = Math.Round(watch.Elapsed.TotalMicroseconds, 1)
            });
        }
        break;
    default:
        throw new ArgumentException($"Unknown checkpoint {lesson}");
}

static Proposal DecisionStub(string prompt) =>
    new("lookup_order", Regex.Match(prompt, @"ORD-[0-9]{4}").Value);

static OrderResult Dispatch(Caller caller, Proposal proposal) =>
    proposal.Name == "lookup_order"
        ? Lookup(caller, new(proposal.OrderId))
        : new("unknown_tool");

static void Log(string step, object value) =>
    Console.WriteLine(JsonSerializer.Serialize(new { step, value }));

static void Require(bool condition)
{
    if (!condition) throw new InvalidOperationException("Checkpoint assertion failed.");
}



record Proposal(string Name, string OrderId);









