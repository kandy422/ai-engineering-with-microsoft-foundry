using System.Net;
using System.Security.Claims;
using System.Text.Json;
using Azure.Core;
using Azure.Data.Tables;
using Microsoft.AspNetCore.Http;
using System.Diagnostics;
using OpenTelemetry;
using OpenTelemetry.Trace;

var ct = CancellationToken.None;
if (args is ["verify-replay",var tableName,var restartTenant,var restartCustomer,var restartActor,var proposalId])
{
    var restarted = new DurableApproval(new TableClient("UseDevelopmentStorage=true",tableName),TimeProvider.System);
    var replay = await restarted.ApproveAsync(new CustomerScope(restartTenant,restartCustomer,restartActor),proposalId,ct);
    Console.WriteLine(JsonSerializer.Serialize(replay));
    return replay is {Status:"already_applied",Writes:0} ? 0 : 1;
}
var tenant = Guid.NewGuid().ToString();
var actor = Guid.NewGuid().ToString();
var scope = new CustomerScope(tenant,"customer'a",actor);
var other = scope with { Customer="other", Actor=Guid.NewGuid().ToString() };
var mappings = new Dictionary<string,string> { [actor]=scope.Customer };
var browserClient = Guid.NewGuid();
ClaimsPrincipal User(string tid,string scopes,bool authenticated=true,Guid? caller=null) =>
    new(new ClaimsIdentity([new Claim("tid",tid),new Claim("oid",actor),new Claim("scp",scopes),
        new Claim("azp",(caller ?? browserClient).ToString())],authenticated?"test":null));
void Check(bool ok,string name)
{
    if (!ok) throw new InvalidOperationException(name);
    Console.WriteLine($"PASS {name}");
}

Check(HostedCapstone.ResolveCustomer(User(tenant,"Support.Access"),tenant,browserClient,mappings)==scope,"registered SPA with delegated scope maps customer");
Check(HostedCapstone.ResolveCustomer(User(other.Actor,"Support.Access"),tenant,browserClient,mappings)==null,"foreign tenant denied");
Check(HostedCapstone.ResolveCustomer(User(tenant,"Support.Access.Read"),tenant,browserClient,mappings)==null,"wrong delegated scope denied");
Check(HostedCapstone.ResolveCustomer(User(tenant,"Support.Access",false),tenant,browserClient,mappings)==null,"unauthenticated claims denied");
Check(HostedCapstone.ResolveCustomer(User(tenant,"Support.Access"),tenant,browserClient,new Dictionary<string,string>())==null,"unmapped user denied");
Check(HostedCapstone.ResolveCustomer(User(tenant,"Support.Access",caller:Guid.NewGuid()),tenant,browserClient,mappings)==null,
    "mapped user with correct delegated scope is denied from a different SPA");
var missingClient = User(tenant,"Support.Access");
var identity = (ClaimsIdentity)missingClient.Identity!;
identity.RemoveClaim(identity.FindFirst("azp")!);
Check(HostedCapstone.ResolveCustomer(missingClient,tenant,browserClient,mappings)==null,
    "delegated token without an authorized client claim is denied");

var clientId = Guid.NewGuid().ToString();
var automation = new Dictionary<string,string> { [clientId] = actor };
ClaimsPrincipal App(string? caller = null, string? objectId = null, string method = "2",
    bool appType = true, bool hasScope = false, bool hasRole = true)
{
    var claims = new List<Claim>
    {
        new("tid",tenant), new("oid",objectId ?? actor), new("azp",caller ?? clientId),
        new("azpacr",method)
    };
    if (hasRole) claims.Add(new("roles","Support.Automation"));
    if (appType) claims.Add(new("idtyp","app"));
    if (hasScope) claims.Add(new("scp","Support.Access"));
    return new(new ClaimsIdentity(claims,"test"));
}
Check(HostedCapstone.ResolveCustomer(App(),tenant,browserClient,mappings,automation)==scope,"enrolled certificate application maps customer");
Check(HostedCapstone.ResolveCustomer(App(hasRole:false),tenant,browserClient,mappings,automation)==null,
    "synthetic certificate claims: mapped enrolled client without required role denied");
Check(HostedCapstone.ResolveCustomer(App(),tenant,browserClient,new Dictionary<string,string>(),automation)==null,
    "synthetic certificate claims: enrolled client with required role and unmapped object denied");
Check(HostedCapstone.ResolveCustomer(App(),tenant,browserClient,mappings,new Dictionary<string,string>())==null,
    "synthetic certificate claims: mapped object with required role and unlisted client denied");
Check(HostedCapstone.ResolveCustomer(App(objectId:other.Actor),tenant,browserClient,
    new Dictionary<string,string> { [other.Actor]=other.Customer },automation)==null,"client and mapped object ID must match enrollment");
Check(HostedCapstone.ResolveCustomer(App(method:"1"),tenant,browserClient,mappings,automation)==null,"secret-authenticated app denied");
Check(HostedCapstone.ResolveCustomer(App(appType:false),tenant,browserClient,mappings,automation)==null,"role without app type denied");
Check(HostedCapstone.ResolveCustomer(App(hasScope:true),tenant,browserClient,mappings,automation)==null,"mixed delegated and automation token denied");
Check(HostedCapstone.ResolveCustomer(App(),tenant,browserClient,mappings)==null,"role-only without enrollment denied");
Check(AttachmentPreflight.Check(new("image/jpeg","extract",200)).State=="routed","supported attachment metadata routes");
Check(AttachmentPreflight.Check(new("image/jpeg","extract",2_000_001)).State=="rejected","oversize attachment metadata denied");

using (var ambient = new Activity("ASP.NET fixture").SetIdFormat(ActivityIdFormat.W3C).Start())
{
    Check(!ambient.Recorded,"ambient request fixture is unsampled");
    var dropped = new ActivityCapture();
    using (var provider = Sdk.CreateTracerProviderBuilder().AddSource(HostedTelemetry.Source.Name)
        .AddProcessor(new SimpleActivityExportProcessor(dropped)).Build())
    {
        using var request = HostedTelemetry.Source.StartActivity("/answer",ActivityKind.Server);
        await HostedTelemetry.DependencyAsync("Foundry chat completion", () => Task.FromResult(1));
    }
    Check(dropped.Spans.Count == 0,"default parent-based sampler drops bounded custom spans");

    var exported = new ActivityCapture();
    using (var provider = HostedTelemetry.ConfigureTracing(Sdk.CreateTracerProviderBuilder())
        .AddProcessor(new SimpleActivityExportProcessor(exported)).Build())
    {
        using var request = HostedTelemetry.Source.StartActivity("/answer",ActivityKind.Server);
        await HostedTelemetry.DependencyAsync("Foundry chat completion", () => Task.FromResult(1));
    }
    var answer = exported.Spans.Single(span => span.DisplayName == "/answer");
    var inference = exported.Spans.Single(span => span.DisplayName == "Foundry chat completion");
    Check(answer.TraceId == ambient.TraceId && answer.ParentSpanId == ambient.SpanId &&
        inference.TraceId == answer.TraceId && inference.ParentSpanId == answer.SpanId,
        "bounded request and dependency export under unsampled parent with intact W3C correlation");
    Check(exported.Spans.All(span => !span.Tags.Any() && !span.Baggage.Any()),
        "bounded trace export contains no request payload or bearer material");
}

await TelemetryWireCheck.RunAsync(Check);

var question = "Can I send an unopened item back?";
var row = new { id="ret",content="Return policy",answer="Unopened items can be returned within 30 days.",
    factKey="returns-window",factValue="30-days",tenantId=tenant,customerScope=scope.Customer };
var crossScopeRow = new {row.id,row.content,row.answer,row.factKey,row.factValue,tenantId=tenant,customerScope=other.Customer};
const string searchUri="https://fixture.search.windows.net/";
const string foundryUri="https://fixture.openai.azure.com/openai/v1/";
var checks = new (string Name, string Text, object Rows, string Draft, string Finish, bool Conflict, string Status)[]
{
    ("unseen paraphrase is forwarded verbatim",question,new[]{row},"{\"citation\":\"ret\"}","stop",false,"answered"),
    ("contradiction outside ranked window",question,new[]{row},"{\"citation\":\"ret\"}","stop",true,"conflicting_evidence"),
    ("unsupported conditions abstain","Can I return an opened item after 90 days?",new[]{row},"{\"citation\":null}","stop",false,"no_evidence"),
    ("empty retrieval skips inference","What about gift cards?",Array.Empty<object>(),"","stop",false,"no_evidence"),
    ("invented ID rejected",question,new[]{row},"{\"citation\":\"absent\"}","stop",false,"no_evidence"),
    ("free-form claim rejected",question,new[]{row},"{\"citation\":\"ret\",\"value\":\"90 days\"}","stop",false,"no_evidence"),
    ("incomplete completion rejected",question,new[]{row},"{\"citation\":\"ret\"}","length",false,"no_evidence")
};
foreach (var check in checks)
{
    int searchCalls=0, modelCalls=0;
    using var http=new HttpClient(new Handler(async request =>
    {
        using var body=JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
        var value=body.RootElement;
        if (request.RequestUri!.Host=="fixture.search.windows.net")
        {
            if (searchCalls++==0)
            {
                Check(value.GetProperty("search").GetString()==check.Text &&
                      value.GetProperty("vectorQueries")[0].GetProperty("text").GetString()==check.Text &&
                      value.GetProperty("queryType").GetString()=="semantic" &&
                      value.GetProperty("vectorFilterMode").GetString()=="preFilter" &&
                      value.GetProperty("filter").GetString()!.Contains("customerScope eq 'customer''a'"),
                      $"hybrid request and escaped scope ({check.Name})");
                return new {value=check.Rows};
            }
            Check(value.GetProperty("filter").GetString()!.Contains("factValue ne '30-days'"),"independent conflict query");
            return new {value=check.Conflict ? new object[]{new {row.id,row.content,row.answer,row.factKey,factValue="90-days",row.tenantId,row.customerScope}} : Array.Empty<object>()};
        }
        modelCalls++;
        Check(value.GetProperty("max_completion_tokens").GetInt32()==1024 &&
              !value.TryGetProperty("temperature",out _) && !value.TryGetProperty("max_tokens",out _),"GPT-5-mini request contract");
        return new {choices=new[]{new {finish_reason=check.Finish,message=new {content=check.Draft}}}};
    }));
    var credential=new TestCredential();
    var search=new ScopedSearch(http,credential,new Uri(searchUri),"capstone","semantic");
    var knowledge=new HostedKnowledge(http,credential,new Uri(foundryUri),"course-chat",search);
    var reply=await knowledge.AnswerAsync(scope,check.Text,ct);
    Check(reply.Status==check.Status && (reply.Status=="answered" ? reply.Value==row.answer && reply.Citation==row.id : reply.Citation is null),check.Name);
    if (check.Rows is object[] rows && rows.Length==0) Check(modelCalls==0,"no model call without evidence");
}
using (var http=new HttpClient(new Handler(_ => Task.FromResult<object>(new {value=new[]{crossScopeRow}}))))
{
    var search = new ScopedSearch(http,new TestCredential(),new Uri(searchUri),"capstone","semantic");
    try { await search.FindAsync(scope,question,ct); throw new InvalidOperationException("Out-of-scope evidence accepted"); }
    catch (JsonException) { Console.WriteLine("PASS out-of-scope returned evidence rejected"); }
}

const string sharedQuestion = "What is my standard delivery window?";
var delivery = new[]
{
    new { Scope = scope, Citation = "SEC-A-DELIVERY", Answer = "Standard delivery takes two business days.", Value = "two-business-days" },
    new { Scope = other, Citation = "SEC-B-DELIVERY", Answer = "Standard delivery takes five business days.", Value = "five-business-days" }
};
foreach (var expected in delivery)
{
    var credential = new TestCredential();
    var scopeLiteral = expected.Citation == "SEC-A-DELIVERY" ? "'customer''a'" : "'other'";
    var filter = $"tenantId eq '{tenant}' and customerScope eq {scopeLiteral} and isActive eq true";
    string? capturedContext = null;
    int searchCalls = 0, modelCalls = 0;
    using var http = new HttpClient(new Handler(async request =>
    {
        using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
        if (request.RequestUri!.Host == "fixture.search.windows.net")
        {
            var sentFilter = body.RootElement.GetProperty("filter").GetString();
            if (searchCalls++ == 0)
            {
                Check(sentFilter == filter && body.RootElement.GetProperty("search").GetString() == sharedQuestion,
                    $"local HTTP transport: exact hybrid scope sent for {expected.Citation}");
                return new {value=new[] {new {
                    id=expected.Citation,content=expected.Answer,answer=expected.Answer,
                    factKey="standard-delivery-window",factValue=expected.Value,
                    tenantId=expected.Scope.Tenant,customerScope=expected.Scope.Customer
                }}};
            }
            Check(sentFilter == $"{filter} and factKey eq 'standard-delivery-window' and factValue ne '{expected.Value}'",
                $"local HTTP transport: conflict probe stays scoped for {expected.Citation}");
            return new {value=Array.Empty<object>()};
        }
        if (request.RequestUri.Host != "fixture.openai.azure.com")
            throw new InvalidOperationException("Unexpected local fixture HTTP destination.");
        modelCalls++;
        var user = body.RootElement.GetProperty("messages").EnumerateArray().Single(message =>
            message.GetProperty("role").GetString() == "user");
        capturedContext = user.GetProperty("content").GetString()!;
        using var context = JsonDocument.Parse(capturedContext);
        var evidence = context.RootElement.GetProperty("evidence").EnumerateArray().ToArray();
        Check(context.RootElement.GetProperty("question").GetString() == sharedQuestion &&
            evidence.Length == 1 &&
            evidence[0].GetProperty("Id").GetString() == expected.Citation &&
            evidence[0].GetProperty("Content").GetString() == expected.Answer &&
            evidence[0].GetProperty("Answer").GetString() == expected.Answer,
            $"local HTTP transport: serialized chat context contains only {expected.Citation}");
        var selected = JsonSerializer.Serialize(new {citation=expected.Citation});
        return new {choices=new[] {new {finish_reason="stop",message=new {content=selected}}}};
    }));
    var search = new ScopedSearch(http,credential,new Uri(searchUri),"capstone","semantic");
    var knowledge = new HostedKnowledge(http,credential,new Uri(foundryUri),"course-chat",search);
    var reply = await knowledge.AnswerAsync(expected.Scope,sharedQuestion,ct);
    Check(searchCalls == 2 && modelCalls == 1 && reply == new Reply("answered",expected.Answer,expected.Citation),
        $"local HTTP transport: one chat call and matching public citation for {expected.Citation}");
    Console.WriteLine(JsonSerializer.Serialize(new {mode="local-fake-http-synthetic-search",fixture=expected.Citation,context=capturedContext}));
}

var table = new TableClient("UseDevelopmentStorage=true","Capstone"+Guid.NewGuid().ToString("N")[..12]);
await table.CreateAsync(ct);
try
{
    var partition=DurableApproval.Partition(scope);
    await table.AddEntityAsync(new TableEntity(partition,"order:ORD-1042") {["Status"]="shipped",["ReceiptReference"]="048271"},ct);
    await table.AddEntityAsync(new TableEntity(partition,"ticket:T-17") {["CallbackRequested"]=false},ct);
    await table.AddEntityAsync(new TableEntity(partition,"ticket:T-18") {["CallbackRequested"]=false},ct);
    var store=new DurableApproval(table,TimeProvider.System);
    Check((await store.OrderAsync(scope,"ORD-1042",ct)).Value=="shipped","scoped order");
    Check((await store.OrderAsync(other,"ORD-1042",ct)).Status=="missing","cross-customer order denied");
    Check((await store.OrderAsync(scope,"1042",ct)).Status=="missing","bare ID rejected");
    var receiptBytes = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory,"fixtures","lumen-receipt.png"),ct);
    var azureBody = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory,"fixtures","lumen-receipt.azure-result.json"),ct);
    var documentEndpoint = new Uri("https://fixture.cognitiveservices.azure.com/");
    var documentCalls = 0;
    var nextResult = azureBody;
    var pollOrigin = documentEndpoint;
    using var documentClient = new HttpClient(new ReceiptHandler(async request =>
    {
        documentCalls++;
        Check(request.Headers.Authorization?.Parameter == "local-test","Document Intelligence request uses bearer credential");
        if (request.Method == HttpMethod.Post)
        {
            Check(request.RequestUri!.AbsolutePath.EndsWith("/prebuilt-receipt:analyze") &&
                request.RequestUri.Query == "?api-version=2024-11-30&pages=1","receipt model and single-page limit");
            using var payload = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            Check(payload.RootElement.GetProperty("base64Source").GetBytesFromBase64().SequenceEqual(receiptBytes),"exact hosted receipt image is submitted");
            var accepted = new HttpResponseMessage(HttpStatusCode.Accepted);
            accepted.Headers.Add("Operation-Location",new Uri(pollOrigin,
                "/documentintelligence/documentModels/prebuilt-receipt/analyzeResults/aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee?api-version=2024-11-30").AbsoluteUri);
            return accepted;
        }
        Check(request.RequestUri!.Host == documentEndpoint.Host && request.RequestUri.Query == "?api-version=2024-11-30",
            "poll stays on configured service and API version");
        return new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent(nextResult,System.Text.Encoding.UTF8,"application/json") };
    }));
    var receipts = new HostedReceipts(store,new ReceiptAnalyzer(documentClient,new TestCredential(),documentEndpoint,TimeProvider.System));
    var foreign = await receipts.VerifyAsync(other,"ORD-1042","image/png",receiptBytes,ct);
    Check(foreign == new Reply("denied") && documentCalls == 0,"foreign order denied without identifiers or provider call");
    Check((await receipts.VerifyAsync(scope,"ORD-1042","image/jpeg",receiptBytes,ct)).Status == "invalid_receipt" &&
        documentCalls == 0,"mismatched image signature blocked before provider");
    var upload = new DefaultHttpContext().Request;
    upload.Headers["X-Order-Id"] = "ORD-1042";
    upload.ContentType = "image/png";
    upload.Body = new MemoryStream(new byte[HostedReceipts.MaxBytes + 1]);
    Check((await receipts.UploadAsync(upload,scope,ct) as IStatusCodeHttpResult)?.StatusCode == 413 &&
        documentCalls == 0,"stream size bound holds without Content-Length");
    using (var observed = JsonDocument.Parse(azureBody))
        Check(ReceiptAnalyzer.ParseReceipt(observed.RootElement).Reference == "048271","exact observed Azure result yields receipt reference");
    var matched = await receipts.VerifyAsync(scope,"ORD-1042","image/png",receiptBytes,ct);
    Check(matched.Status == "receipt_matched" && matched.Value!.Contains("ORD-1042 matches receipt reference 048271"),
        "authorized canonical order corroborated from captured Azure response");
    nextResult = azureBody.Replace("048271","048272",StringComparison.Ordinal);
    Check(await receipts.VerifyAsync(scope,"ORD-1042","image/png",receiptBytes,ct) == new Reply("receipt_review"),
        "mismatch withheld without echoing extracted or canonical identifier");
    pollOrigin = new Uri("https://other.cognitiveservices.azure.com/");
    var beforeRejectedPoll = documentCalls;
    try
    {
        await receipts.VerifyAsync(scope,"ORD-1042","image/png",receiptBytes,ct);
        throw new InvalidOperationException("Untrusted polling origin accepted");
    }
    catch (JsonException)
    {
        Check(documentCalls == beforeRejectedPoll + 1,"cross-origin poll rejected before forwarding bearer");
    }
    var proposal=await store.ProposeAsync(scope,"T-17","Request callback",ct);
    Check(proposal.Status=="approval_required" && proposal.Writes==0,"proposal writes no ticket state");
    Check((await store.ApproveAsync(other,proposal.Value,ct)).Status=="denied","cross-customer approval denied");
    Check((await store.ApproveAsync(scope with {Actor=other.Actor},proposal.Value,ct)).Status=="denied","different actor in same scope denied");
    var second=await store.ProposeAsync(scope,"T-17","Request callback",ct);
    var concurrent=await Task.WhenAll(store.ApproveAsync(scope,proposal.Value,ct),store.ApproveAsync(scope,proposal.Value,ct),
        store.ApproveAsync(scope,second.Value,ct));
    Check(concurrent.Sum(x=>x.Writes)==1 && concurrent.All(x=>x.Status is "applied" or "already_applied"),"concurrent duplicate and separate proposals cause one write");
    var info = new System.Diagnostics.ProcessStartInfo("dotnet") {UseShellExecute=false,RedirectStandardOutput=true};
    foreach (var argument in new[]{System.Reflection.Assembly.GetExecutingAssembly().Location,"verify-replay",table.Name,
                                  tenant,scope.Customer,actor,proposal.Value!})
        info.ArgumentList.Add(argument);
    using var restarted = System.Diagnostics.Process.Start(info)!;
    var replayOutput = await restarted.StandardOutput.ReadToEndAsync(ct);
    await restarted.WaitForExitAsync(ct);
    Console.WriteLine(replayOutput.Trim());
    Check(restarted.ExitCode==0,"fresh process reads durable replay result");
    Check((await table.GetEntityAsync<TableEntity>(partition,"ticket:T-17",cancellationToken:ct)).Value.GetBoolean("CallbackRequested")==true,
        "ticket state persisted with approval");
    var expired=await store.ProposeAsync(scope,"T-18","Request callback",ct);
    Check((await new DurableApproval(table,new FutureClock()).ApproveAsync(scope,expired.Value,ct)).Status=="approval_expired","expired approval writes nothing");
}
finally { await table.DeleteAsync(ct); }
return 0;

sealed class FutureClock : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => DateTimeOffset.UtcNow.AddHours(1);
}

sealed class Handler(Func<HttpRequestMessage,Task<object>> response) : HttpMessageHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken) =>
        new(HttpStatusCode.OK) {Content=System.Net.Http.Json.JsonContent.Create(await response(request))};
}

sealed class ReceiptHandler(Func<HttpRequestMessage,Task<HttpResponseMessage>> response) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken) =>
        response(request);
}

sealed class ActivityCapture : BaseExporter<Activity>
{
    internal List<Activity> Spans { get; } = [];

    public override ExportResult Export(in Batch<Activity> batch)
    {
        foreach (var span in batch) Spans.Add(span);
        return ExportResult.Success;
    }
}

sealed class TestCredential : TokenCredential
{
    public override AccessToken GetToken(TokenRequestContext requestContext,CancellationToken cancellationToken) =>
        new("local-test",DateTimeOffset.UtcNow.AddMinutes(5));
    public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext,CancellationToken cancellationToken) =>
        ValueTask.FromResult(GetToken(requestContext,cancellationToken));
}
