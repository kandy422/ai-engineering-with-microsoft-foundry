using System.Security.Claims;
using System.Text.Json.Serialization;
using Azure.Core;
using Azure.Data.Tables;
using Azure.Identity;
using Microsoft.AspNetCore.Authentication.JwtBearer;

static class HostedCapstone
{
    internal static async Task RunAsync(WebApplicationBuilder builder)
    {
        var cfg = builder.Configuration;
        var tenant = Guid.Parse(Required(cfg, "Identity:TenantId")).ToString();
        var audience = Guid.Parse(Required(cfg, "Identity:Audience")).ToString();
        var browserClient = Guid.Parse(Required(cfg, "Identity:BrowserClientId"));
        var mappings = cfg.GetSection("Identity:Customers").Get<Dictionary<string, string>>() ?? [];
        if (mappings.Count == 0 || mappings.Any(x => !Guid.TryParseExact(x.Key, "D", out _) ||
                string.IsNullOrWhiteSpace(x.Value) || x.Value.Length > 100))
            throw new InvalidOperationException("Configure object IDs mapped to authorized customer scopes.");
        var customers = mappings.ToDictionary(x => Guid.Parse(x.Key).ToString(),x => x.Value);
        var automation = cfg.GetSection("Identity:Automation").Get<Dictionary<string, string>>() ?? [];
        var enrolled = automation.ToDictionary(x => Guid.Parse(x.Key).ToString(), x => Guid.Parse(x.Value).ToString());
        if (enrolled.Values.Any(actor => !customers.ContainsKey(actor)))
            throw new InvalidOperationException("Every automation principal must have a customer mapping.");
        var issuer = $"https://login.microsoftonline.com/{tenant}/v2.0";
        builder.Logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);
        if (cfg["APPLICATIONINSIGHTS_CONNECTION_STRING"] is { Length: > 0 } connectionString)
            HostedTelemetry.AddAzureMonitor(builder, options => options.ConnectionString = connectionString);
        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
        {
            options.Authority = issuer;
            options.Audience = audience;
            options.MapInboundClaims = false;
            options.TokenValidationParameters.ValidIssuer = issuer;
            options.TokenValidationParameters.ValidateIssuer = true;
            options.TokenValidationParameters.ValidateIssuerSigningKey = true;
            options.TokenValidationParameters.ValidAlgorithms = ["RS256"];
            options.TokenValidationParameters.IgnoreTrailingSlashWhenValidatingAudience = false;
        });
        builder.Services.AddAuthorizationBuilder().AddPolicy("customer", policy => policy.RequireAuthenticatedUser()
            .RequireAssertion(context => ResolveCustomer(context.User, tenant, browserClient, customers, enrolled) is not null));
        builder.Services.ConfigureHttpJsonOptions(o =>
            o.SerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow);

        TokenCredential credential = new ManagedIdentityCredential(ManagedIdentityId.SystemAssigned);
        using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
            { Timeout = TimeSpan.FromSeconds(30) };
        var index = Required(cfg, "Search:Index");
        var retrieval = new ScopedSearch(http, credential, Endpoint(cfg,"Search:Endpoint",".search.windows.net","/"),
            index, Required(cfg,"Search:SemanticConfiguration"));
        var knowledge = new HostedKnowledge(http, credential,
            Endpoint(cfg,"Foundry:Endpoint",".openai.azure.com","/openai/v1/"),
            Required(cfg,"Foundry:Deployment"), retrieval);
        var store = new DurableApproval(new TableClient(
            Endpoint(cfg,"Storage:Endpoint",".table.core.windows.net","/"),
            Required(cfg,"Storage:Table"), credential), TimeProvider.System);
        var receipts = string.IsNullOrEmpty(cfg["Document:Endpoint"]) ? null : new HostedReceipts(store,
            new ReceiptAnalyzer(http,credential,Endpoint(cfg,"Document:Endpoint",".cognitiveservices.azure.com","/"),TimeProvider.System));
        var app = builder.Build();
        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger(HostedTelemetry.Source.Name);
        app.Use(async (context, next) =>
        {
            var route = (context.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText;
            var tracked = route is "/answer" or "/order" or "/propose" or "/approve" or "/attachment/preflight" or "/attachment";
            using var operation = tracked
                ? HostedTelemetry.Source.StartActivity(route!, System.Diagnostics.ActivityKind.Server) : null;
            var trace = System.Diagnostics.Activity.Current?.TraceId.ToString() ?? context.TraceIdentifier;
            context.Response.Headers["X-Correlation-Id"] = trace;
            context.Response.Headers["Cache-Control"] = "no-store";
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            context.Response.Headers["Referrer-Policy"] = "no-referrer";
            var frameAncestors = context.Request.Path == "/hosted/redirect.html" ? "'self'" : "'none'";
            context.Response.Headers["Content-Security-Policy"] =
                $"default-src 'self'; connect-src 'self' https://login.microsoftonline.com; frame-src 'self' https://login.microsoftonline.com; frame-ancestors {frameAncestors}; base-uri 'none'; form-action 'self'";
            try { await next(context); }
            finally
            {
                if (tracked)
                {
                    operation?.SetTag("http.response.status_code",context.Response.StatusCode);
                    if (context.Response.StatusCode >= 500) operation?.SetStatus(System.Diagnostics.ActivityStatusCode.Error);
                    logger.LogInformation("Capstone {Route} {Status} Trace={TraceId}", route, context.Response.StatusCode, trace);
                }
            }
        });
        app.MapGet("/", () => Results.Redirect("/hosted/"));
        app.UseDefaultFiles(new DefaultFilesOptions { RequestPath = "/hosted", FileProvider =
            new Microsoft.Extensions.FileProviders.PhysicalFileProvider(Path.Combine(app.Environment.WebRootPath,"hosted")) });
        app.UseStaticFiles(new StaticFileOptions { RequestPath = "/hosted", FileProvider =
            new Microsoft.Extensions.FileProviders.PhysicalFileProvider(Path.Combine(app.Environment.WebRootPath,"hosted")) });
        app.UseAuthentication();
        app.UseAuthorization();
        app.Use(async (context, next) =>
        {
            try { await next(context); }
            catch (Exception ex) when ((ex is Azure.RequestFailedException or HttpRequestException or
                Azure.Identity.AuthenticationFailedException or OperationCanceledException or System.Text.Json.JsonException)
                && !context.RequestAborted.IsCancellationRequested && !context.Response.HasStarted)
            {
                logger.LogWarning("Capstone dependency failed. Trace={TraceId} Error={Error}",
                    System.Diagnostics.Activity.Current?.TraceId.ToString() ?? context.TraceIdentifier, ex.GetType().Name);
                context.Response.StatusCode = 503;
                await context.Response.WriteAsJsonAsync(new Reply("unavailable"), context.RequestAborted);
            }
        });
        app.MapGet("/health", () => new { status = "ready", mode = "hosted", index });
        app.MapGet("/hosted/config", () => new { tenantId = tenant, clientId = browserClient, scope = $"api://{audience}/Support.Access", receiptsEnabled = receipts is not null });
        var api = app.MapGroup("/").RequireAuthorization("customer");
        CustomerScope Scope(ClaimsPrincipal user) => ResolveCustomer(user,tenant,browserClient,customers,enrolled)!;
        api.MapPost("/answer", async (HostedQuestion request, ClaimsPrincipal user, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Text) || request.Text.Length > 1500)
                return new Reply("clarify", "Enter a specific policy question, up to 1500 characters.");
            return await knowledge.AnswerAsync(Scope(user), request.Text, ct);
        });
        api.MapPost("/order", (HostedOrder request, ClaimsPrincipal user, CancellationToken ct) =>
            store.OrderAsync(Scope(user), request.Id, ct));
        api.MapPost("/propose", (HostedChange request, ClaimsPrincipal user, CancellationToken ct) =>
            store.ProposeAsync(Scope(user), request.Ticket, request.Action, ct));
        api.MapPost("/approve", (HostedApproval request, ClaimsPrincipal user, CancellationToken ct) =>
            store.ApproveAsync(Scope(user), request.Proposal, ct));
        api.MapPost("/attachment/preflight", (AttachmentMetadata request) => AttachmentPreflight.Check(request));
        api.MapPost("/attachment", (HttpRequest request, ClaimsPrincipal user, CancellationToken ct) =>
            receipts is null ? Task.FromResult(Results.Json(new Reply("unavailable"), statusCode: 503))
                : receipts.UploadAsync(request,Scope(user),ct));
        await app.RunAsync();
    }

    internal static CustomerScope? ResolveCustomer(ClaimsPrincipal user, string tenant, Guid browserClient,
        IReadOnlyDictionary<string,string> customers, IReadOnlyDictionary<string,string>? automation = null)
    {
        var objectId = user.FindFirstValue("oid");
        if (user.Identity?.IsAuthenticated != true ||
            !string.Equals(user.FindFirstValue("tid"),tenant,StringComparison.OrdinalIgnoreCase) ||
            !Guid.TryParse(objectId,out var actor) || !customers.TryGetValue(actor.ToString(),out var customer) ||
            !Guid.TryParse(user.FindFirstValue("azp"),out var client))
            return null;
        var scope = user.FindFirstValue("scp");
        if (user.FindFirstValue("idtyp") == "app")
        {
            if (scope is not null || user.FindFirstValue("azpacr") != "2" ||
                !user.HasClaim("roles","Support.Automation") ||
                automation is null ||
                !automation.TryGetValue(client.ToString(),out var enrolledActor) || enrolledActor != actor.ToString())
                return null;
        }
        else if (client != browserClient || scope?.Split(' ').Contains("Support.Access",StringComparer.Ordinal) != true ||
                 user.HasClaim("roles","Support.Automation"))
            return null;
        return new CustomerScope(tenant,customer,actor.ToString());
    }

    private static string Required(IConfiguration config, string key) =>
        config[key] is { Length: > 0 } value ? value : throw new InvalidOperationException($"Configure {key}.");

    private static Uri Endpoint(IConfiguration config, string key, string suffix, string path)
    {
        if (!Uri.TryCreate(Required(config,key),UriKind.Absolute,out var uri) ||
            uri.Scheme!="https" || !uri.IsDefaultPort || !uri.Host.EndsWith(suffix,StringComparison.OrdinalIgnoreCase) ||
            uri.AbsolutePath!=path || uri.UserInfo!="" || uri.Query!="" || uri.Fragment!="")
            throw new InvalidOperationException($"Invalid Azure endpoint for {key}.");
        return uri;
    }
}

record CustomerScope(string Tenant, string Customer, string Actor);
record HostedQuestion(string Text);
record HostedOrder(string Id);
record HostedChange(string Ticket, string Action);
record HostedApproval(string Proposal);
