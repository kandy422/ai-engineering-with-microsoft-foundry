using System.Net.Http.Headers;
using System.Text.Json;
using Azure.Core;

record SearchPassage(string Id, string Content, string Answer, string FactKey, string FactValue);
interface IScopedSearch
{
    Task<SearchPassage[]> FindAsync(CustomerScope scope, string question, CancellationToken ct);
    Task<bool> ConflictsAsync(CustomerScope scope, SearchPassage passage, CancellationToken ct);
}

sealed class ScopedSearch(HttpClient http, TokenCredential credential, Uri endpoint, string index, string semanticConfiguration) : IScopedSearch
{
    private readonly Uri query = new(endpoint,$"indexes/{Uri.EscapeDataString(index)}/docs/search?api-version=2026-04-01");
    private const string Fields = "id,content,answer,factKey,factValue,tenantId,customerScope";

    public async Task<SearchPassage[]> FindAsync(CustomerScope scope, string question, CancellationToken ct)
    {
        return await SendAsync(scope, new
        {
            search = question,
            queryType = "semantic",
            semanticConfiguration,
            vectorFilterMode = "preFilter",
            vectorQueries = new[] { new { kind = "text", text = question, fields = "contentVector", k = 50 } },
            filter = Filter(scope),
            select = Fields,
            top = 8
        }, ct);
    }

    // Probe all scoped active rows for an incompatible value of the selected fact,
    // including rows excluded from the initial ranked window.
    public async Task<bool> ConflictsAsync(CustomerScope scope, SearchPassage passage, CancellationToken ct) =>
        (await SendAsync(scope,new
        {
            search = "*",
            filter = $"{Filter(scope)} and factKey eq {Literal(passage.FactKey)} and factValue ne {Literal(passage.FactValue)}",
            select = Fields,
            top = 1
        },ct)).Length != 0;

    internal static string Filter(CustomerScope scope) =>
        $"tenantId eq {Literal(scope.Tenant)} and customerScope eq {Literal(scope.Customer)} and isActive eq true";
    private static string Literal(string value) => "'" + value.Replace("'","''",StringComparison.Ordinal) + "'";

    private async Task<SearchPassage[]> SendAsync(CustomerScope scope, object body, CancellationToken ct)
    {
        using var response = await HostedTelemetry.DependencyAsync("Azure AI Search query", () =>
            AzureJson.PostAsync(http,credential,"https://search.azure.com/.default",query,body,ct));
        if (!response.RootElement.TryGetProperty("value",out var rows) || rows.ValueKind!=JsonValueKind.Array)
            throw new JsonException("Search response missing its result array.");
        var result = new List<SearchPassage>();
        foreach (var row in rows.EnumerateArray())
        {
            if (Read(row,"tenantId")!=scope.Tenant || Read(row,"customerScope")!=scope.Customer)
                throw new JsonException("Search returned an out-of-scope document.");
            result.Add(new(Read(row,"id"),Read(row,"content"),Read(row,"answer"),Read(row,"factKey"),Read(row,"factValue")));
        }
        return result.ToArray();
    }

    private static string Read(JsonElement row,string field) =>
        row.TryGetProperty(field,out var value) && value.ValueKind==JsonValueKind.String &&
        value.GetString() is { Length: > 0 } text ? text : throw new JsonException($"Search result missing {field}.");
}

static class AzureJson
{
    internal static async Task<JsonDocument> PostAsync(HttpClient http,TokenCredential credential,string scope,Uri uri,object body,CancellationToken ct)
    {
        var token = await credential.GetTokenAsync(new TokenRequestContext([scope]),ct);
        using var request = new HttpRequestMessage(HttpMethod.Post,uri) { Content=JsonContent.Create(body) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer",token.Token);
        using var response = await http.SendAsync(request,ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken:ct)
            ?? throw new JsonException("Empty service response.");
    }
}
