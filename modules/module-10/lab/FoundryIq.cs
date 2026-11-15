using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

static class FoundryIq
{
    public static async Task<object> Compare()
    {
        string Required(string name) => Environment.GetEnvironmentVariable(name) is { Length: > 0 } value
            ? value : throw new InvalidOperationException($"Set {name} for the configured test environment.");
        var endpoint = new Uri(Required("RAG_IQ_SEARCH_ENDPOINT"));
        if (endpoint.Scheme != Uri.UriSchemeHttps || !endpoint.IsDefaultPort || endpoint.UserInfo.Length > 0 || !endpoint.Host.EndsWith(".search.windows.net", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Use the Azure public cloud HTTPS Search endpoint.");
        var kb = Uri.EscapeDataString(Required("RAG_IQ_KNOWLEDGE_BASE"));
        var source = Required("RAG_IQ_INDEX_SOURCE");
        var stable = Environment.GetEnvironmentVariable("RAG_IQ_MODE") switch
        {
            "stable" => true,
            "acl" => false,
            _ => throw new InvalidOperationException("Set RAG_IQ_MODE to stable or acl.")
        };
        var api = stable ? "2026-04-01" : "2026-08-01-preview";
        var uniqueMarker = "course10-restricted-control";
        var publicMarker = "course10-public-control";
        var query = Required("RAG_IQ_TEST_QUESTION");
        if (query.Length > 256) throw new InvalidOperationException("Use a query of at most 256 characters.");
        var serviceToken = Required("RAG_IQ_SEARCH_TOKEN");
        var userToken = stable ? null : Required("RAG_IQ_USER_TOKEN");
        var expectRestricted = !stable && Required("RAG_IQ_ACL_STAGE") switch
        {
            "granted" => true,
            "denied" or "revoked" => false,
            _ => throw new InvalidOperationException("Set RAG_IQ_ACL_STAGE to denied, granted or revoked.")
        };
        var requestBody = new Dictionary<string, object>
        {
            ["intents"] = new[] { new { type = "semantic", search = query } },
            ["knowledgeSourceParams"] = new[] { new { knowledgeSourceName = source, kind = "searchIndex",
                includeReferences = true, includeReferenceSourceData = true } },
            [stable ? "maxOutputSizeInTokens" : "maxOutputSize"] = 5001,
            ["maxRuntimeInSeconds"] = 30
        };
        if (!stable)
        {
            requestBody["outputMode"] = "extractiveData";
            requestBody["retrievalReasoningEffort"] = new { kind = "minimal" };
            requestBody["includeActivity"] = true;
        }
        using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { BaseAddress = endpoint };
        async Task<(JsonElement Payload, object Receipt)> Retrieve(string? token)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post,
                $"/knowledgebases('{kb}')/retrieve?api-version={api}");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", serviceToken);
            if (token is not null) request.Headers.Add("x-ms-query-source-authorization", token);
            request.Content = JsonContent.Create(requestBody);
            using var response = await client.SendAsync(request);
            if (!response.IsSuccessStatusCode)
                Console.Error.WriteLine(await response.Content.ReadAsStringAsync());
            response.EnsureSuccessStatusCode();
            var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
            var requestId = response.Headers.TryGetValues("request-id", out var ids) ? ids.FirstOrDefault() : null;
            return (payload, new { status = (int)response.StatusCode, requestId, date = response.Headers.Date });
        }
        var (response, receipt) = await Retrieve(userToken);
        var responseText = response.ToString();
        var references = response.GetProperty("references").EnumerateArray().ToArray();
        bool HasReference(string docKey, string marker) => references.Any(reference =>
            reference.TryGetProperty("docKey", out var key) && key.GetString() == docKey
            && reference.TryGetProperty("sourceData", out var data)
            && data.TryGetProperty("marker", out var value) && value.GetString() == marker);
        var publicControl = HasReference("returns-unopened", publicMarker);
        var restrictedReference = HasReference("returns-unopened-internal", uniqueMarker);
        var restrictedAnywhere = responseText.Contains(uniqueMarker, StringComparison.Ordinal)
            || responseText.Contains("returns-unopened-internal", StringComparison.Ordinal)
            || responseText.Contains("station Indigo", StringComparison.OrdinalIgnoreCase);
        var passed = publicControl && (expectRestricted ? restrictedReference : !restrictedAnywhere);
        if (!passed)
        {
            Console.Error.WriteLine("Visibility check failed. Keep the captured response for diagnosis.");
            Environment.ExitCode = 1;
        }
        return new { api, requestBody, receipt, response, publicControl, restrictedReference, restrictedAnywhere, passed };
    }
}
