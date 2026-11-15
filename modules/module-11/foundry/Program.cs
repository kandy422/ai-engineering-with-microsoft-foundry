#pragma warning disable OPENAI001
using Azure.AI.Extensions.OpenAI;
using Azure.AI.Projects;
using Azure.AI.Projects.Agents;
using Azure.Identity;
using OpenAI.Responses;
using System.ClientModel.Primitives;
using System.Text.Json;
using System.Text.RegularExpressions;

const string tool = "lookup_order";
const string instructions = """
For every order-status question call lookup_order exactly once with the requested identifier.
Use only its result. For not_available, say "Order status is unavailable." Do not infer ownership.
""";
const string description = "Read the current status for an order visible to the caller.";
const string schema = """{"type":"object","properties":{"orderId":{"type":"string","pattern":"^ORD-[0-9]{4}$"}},"required":["orderId"],"additionalProperties":false}""";
const int maxOutputTokens = 512;
var cases = new[]
{
    (Id: "ORD-1042", Code: "ok", Status: (string?)"Shipped"),
    (Id: "ORD-2045", Code: "not_available", Status: (string?)null)
};

if (args is [] or ["--local"])
{
    foreach (var test in cases)
        Check(Dispatch(tool, JsonSerializer.Serialize(new { orderId = test.Id }), test.Id), test.Code, test.Status);
    Check(Dispatch(tool, """{"orderId":1042}""", "ORD-1042"), "invalid_argument", null);
    Check(Dispatch(tool, """{"orderId":"ORD-2045","caller":"sam"}""", "ORD-2045"), "invalid_argument", null);
    Check(Dispatch("cancel_order", """{"orderId":"ORD-1042"}""", "ORD-1042"), "unknown_tool", null);
    Check(Dispatch(tool, """{"orderId":"ORD-2045"}""", "ORD-1042"), "unexpected_order", null);
    Log("local-checks", new { checks = 6, scope = "fictional fixture; no cloud requests" });
    return;
}

if (args is not ["--run-authorized-cloud-demo"])
    throw new ArgumentException("Use --local or --run-authorized-cloud-demo.");
var endpoint = new Uri(Required("FOUNDRY_PROJECT_ENDPOINT"));
if (!endpoint.IsAbsoluteUri || endpoint.Scheme != Uri.UriSchemeHttps ||
    !endpoint.Host.EndsWith(".services.ai.azure.com", StringComparison.OrdinalIgnoreCase) ||
    !Regex.IsMatch(endpoint.AbsolutePath, @"^/api/projects/[^/]+/?$") ||
    endpoint.UserInfo.Length != 0 || endpoint.Query.Length != 0 || endpoint.Fragment.Length != 0 ||
    !endpoint.IsDefaultPort)
    throw new ArgumentException("Use the HTTPS Foundry project endpoint.");
var allocation = Required("FOUNDRY_RECORDING_ALLOCATION");
var deployment = Required("FOUNDRY_MODEL_DEPLOYMENT");
var name = Required("FOUNDRY_AGENT_NAME");
using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(4));
var ct = deadline.Token;
var options = new AIProjectClientOptions(AIProjectClientOptions.ServiceVersion.V1)
{
    RetryPolicy = new ClientRetryPolicy(0),
    NetworkTimeout = TimeSpan.FromSeconds(60)
};
var project = new AIProjectClient(endpoint, new AzureCliCredential(), options);
var definition = new DeclarativeAgentDefinition(deployment)
{
    Instructions = instructions,
    Tools = { ResponseTool.CreateFunctionTool(tool, BinaryData.FromString(schema), true, description) }
};
Log("allocation", new { allocation, endpoint, deployment, name, maxOutputTokens, maximumResponses = 4, instructions, description, schema });
var agent = (await project.AgentAdministrationClient.CreateAgentVersionAsync(name, new(definition), cancellationToken: ct)).Value;
Log("agent-created", new { agent.Id, agent.Name, agent.Version });
var conversations = project.ProjectOpenAIClient.GetProjectConversationsClient();
var applicationAccepted = true;
foreach (var test in cases)
{
    var conversation = (await conversations.CreateProjectConversationAsync(cancellationToken: ct)).Value;
    var client = project.ProjectOpenAIClient.GetProjectResponsesClientForAgent(
        new Azure.AI.Extensions.OpenAI.AgentReference(agent.Name, agent.Version), conversation.Id);
    var prompt = $"What is the current status of order {test.Id}?";
    Log("first-request", new { conversationId = conversation.Id, prompt, orderId = test.Id, callerFixture = "alex" });
    var first = (await client.CreateResponseAsync(Request(ResponseItem.CreateUserMessageItem(prompt)), ct)).Value;
    LogResponse("first-response", conversation.Id, first);
    var calls = first.OutputItems.OfType<FunctionCallResponseItem>().ToArray();
    if (calls.Length != 1 || string.IsNullOrWhiteSpace(calls[0].CallId))
    {
        applicationAccepted = false;
        Log("case-rejected", new { test.Id, reason = "expected exactly one correlated tool call", count = calls.Length });
        continue;
    }

    var call = calls[0];
    var arguments = call.FunctionArguments.ToString();
    var result = Dispatch(call.FunctionName, arguments, test.Id);
    Log("local-tool-result", new { conversation.Id, responseId = first.Id, call.CallId, call.FunctionName, arguments, result });
    var output = JsonSerializer.Serialize(result);
    Log("submit-tool-output", new { conversation.Id, call.CallId, output });
    var final = (await client.CreateResponseAsync(Request(ResponseItem.CreateFunctionCallOutputItem(call.CallId, output)), ct)).Value;
    LogResponse("final-response", conversation.Id, final);
    var accepted = result == new OrderResult(test.Code, test.Status) &&
        !final.OutputItems.OfType<FunctionCallResponseItem>().Any() && !string.IsNullOrWhiteSpace(final.GetOutputText());
    applicationAccepted &= accepted;
    Log("case-check", new { test.Id, accepted, finalTextReview = "required: verify Shipped for the allowed order and no status disclosure for the denied order", portalTraceReview = "required" });
}
Environment.ExitCode = applicationAccepted ? 0 : 2;

CreateResponseOptions Request(ResponseItem item) => new()
{
    MaxOutputTokenCount = maxOutputTokens,
    ParallelToolCallsEnabled = false,
    InputItems = { item }
};

OrderResult Dispatch(string name, string arguments, string expectedOrder)
{
    if (name != tool) return new("unknown_tool");
    if (arguments.Length > 512) return new("invalid_argument");
    var result = Orders.Invoke(new("alex"), arguments);
    if (result.Code == "invalid_argument") return result;
    using var parsed = JsonDocument.Parse(arguments);
    return parsed.RootElement.GetProperty("orderId").GetString() == expectedOrder
        ? result : new("unexpected_order");
}

static void Check(OrderResult result, string code, string? status)
{
    if (result != new OrderResult(code, status))
        throw new InvalidOperationException($"Expected {code}/{status}; got {result}.");
    Log("local-result", result);
}

static void LogResponse(string step, string conversationId, ResponseResult response) =>
    Log(step, new
    {
        conversationId, response.Id, response.Status, response.Usage,
        text = response.GetOutputText(), calls = response.OutputItems.OfType<FunctionCallResponseItem>()
            .Select(c => new { c.CallId, c.FunctionName, arguments = c.FunctionArguments.ToString() }).ToArray()
    });

static string Required(string name) =>
    Environment.GetEnvironmentVariable(name) is { Length: > 0 } value ? value :
        throw new InvalidOperationException($"Set {name} from the coordinator allocation.");

static void Log(string step, object data) =>
    Console.WriteLine(JsonSerializer.Serialize(new { atUtc = DateTimeOffset.UtcNow, step, data }));




