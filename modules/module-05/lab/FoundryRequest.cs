using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

internal static class FoundryRequest
{
    public static async Task<JsonElement> SendAsync(string file)
    {
        var endpoint = new Uri(Environment.GetEnvironmentVariable("FOUNDRY_OPENAI_V1_BASE")
                               ?? throw new InvalidOperationException("Set FOUNDRY_OPENAI_V1_BASE."));
        if (endpoint.Scheme != Uri.UriSchemeHttps ||
            !endpoint.Host.EndsWith(".openai.azure.com", StringComparison.OrdinalIgnoreCase) ||
            endpoint.AbsolutePath != "/openai/v1/")
            throw new ArgumentException("Use the Azure OpenAI v1 HTTPS base URL.");
        var token = Environment.GetEnvironmentVariable("FOUNDRY_ENTRA_TOKEN")
                    ?? throw new InvalidOperationException("Set FOUNDRY_ENTRA_TOKEN for this process.");
        var body = await File.ReadAllTextAsync(file);
        using var json = JsonDocument.Parse(body);
        if (json.RootElement.GetProperty("model").GetString() != "course-chat" ||
            json.RootElement.GetProperty("max_completion_tokens").GetInt32() is < 1 or > 2048)
            throw new ArgumentException("Use a bounded Module 5 course-chat request.");
        using var handler = new HttpClientHandler { AllowAutoRedirect = false };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(120) };
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(endpoint, "chat/completions"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        using var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        using var result = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return result.RootElement.Clone();
    }
}
