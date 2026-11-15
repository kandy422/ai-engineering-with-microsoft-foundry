using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;

public sealed record Turn(string Role, string Content);
public sealed record ChatRequest(Turn[] Messages, string Scenario = "normal");
public sealed record TokenUsage(int InputTokens, int OutputTokens);
public sealed record ModelEvent(string? Delta = null, TokenUsage? Usage = null);
public interface IChatModel
{
    IAsyncEnumerable<ModelEvent> StreamAsync(Turn[] messages, string scenario, CancellationToken cancellation);
}

public sealed class LocalStub : IChatModel
{
    public async IAsyncEnumerable<ModelEvent> StreamAsync(
        Turn[] messages, string scenario, [EnumeratorCancellation] CancellationToken cancellation)
    {
        if (scenario == "throttle")
            throw new HttpRequestException("Simulated throttling", null, HttpStatusCode.TooManyRequests);
        if (scenario == "timeout")
            await Task.Delay(TimeSpan.FromSeconds(30), cancellation);

        var first = messages.First(t => t.Role == "user").Content;
        foreach (var chunk in new[] { "I can help with: ", first, ". Please share your order reference." })
        {
            await Task.Delay(350, cancellation);
            yield return new ModelEvent(Delta: chunk);
        }
    }
}

public sealed record FoundryChatOptions(
    bool RetryOnThrottle = true,
    string? ReasoningEffort = null,
    string? Subscription = null);

public sealed class FoundryChat(
    HttpClient http, Uri endpoint, string deployment,
    Func<CancellationToken, Task<string>>? tokenProvider = null,
    FoundryChatOptions? options = null) : IChatModel
{
    public async IAsyncEnumerable<ModelEvent> StreamAsync(
        Turn[] messages, string scenario, [EnumeratorCancellation] CancellationToken cancellation)
    {
        using var response = await OpenAsync(messages, cancellation);
        using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(cancellation));
        string? finish = null;
        while (await reader.ReadLineAsync(cancellation) is { } line)
        {
            if (!line.StartsWith("data: ", StringComparison.Ordinal)) continue;
            if (line == "data: [DONE]")
            {
                if (finish == "stop") yield break;
                throw new IOException("Model did not complete the reply.");
            }

            using var chunk = JsonDocument.Parse(line[6..]);
            if (chunk.RootElement.TryGetProperty("usage", out var usage) && usage.ValueKind == JsonValueKind.Object)
                yield return new ModelEvent(Usage: new TokenUsage(
                    usage.GetProperty("prompt_tokens").GetInt32(), usage.GetProperty("completion_tokens").GetInt32()));
            if (!chunk.RootElement.TryGetProperty("choices", out var choices))
                throw new IOException("Missing choices in model stream.");
            foreach (var choice in choices.EnumerateArray())
            {
                if (choice.TryGetProperty("finish_reason", out var reason) && reason.ValueKind == JsonValueKind.String)
                    finish = reason.GetString();
                if (choice.TryGetProperty("delta", out var delta) && delta.TryGetProperty("content", out var content) &&
                    content.ValueKind == JsonValueKind.String)
                    yield return new ModelEvent(Delta: content.GetString());
            }
        }
        throw new IOException("Stream ended without its terminal event.");
    }

    private static readonly Turn SupportInstructions = new("system",
        "Draft brief customer-support replies. If the order reference is absent, ask for it. Never infer an order status or delivery date.");

    private async Task<HttpResponseMessage> OpenAsync(Turn[] history, CancellationToken cancellation)
    {
        Turn[] messages = [SupportInstructions, ..history];
        var token = tokenProvider is null
            ? await CliTokenAsync(options?.Subscription, cancellation)
            : await tokenProvider(cancellation);
        for (var attempt = 0; ; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(endpoint, "chat/completions"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            var body = new Dictionary<string, object>
            {
                ["model"] = deployment,
                ["messages"] = messages,
                ["stream"] = true,
                ["max_completion_tokens"] = 256,
                ["stream_options"] = new { include_usage = true }
            };
            if (options?.ReasoningEffort is { } effort) body["reasoning_effort"] = effort;
            request.Content = JsonContent.Create(body);
            var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellation);
            if (response.IsSuccessStatusCode) return response;

            var status = response.StatusCode;
            var retryAfter = response.Headers.RetryAfter;
            var delay = retryAfter?.Delta
                ?? (retryAfter?.Date is { } date ? date - DateTimeOffset.UtcNow : TimeSpan.FromSeconds(1));
            response.Dispose();
            if (options?.RetryOnThrottle == false || status != HttpStatusCode.TooManyRequests || attempt >= 1 || delay > TimeSpan.FromSeconds(3))
                throw new HttpRequestException("Foundry rejected the request.", null, status);
            await Task.Delay(delay > TimeSpan.Zero ? delay : TimeSpan.Zero, cancellation);
        }
    }

    private static Process StartCli(ProcessStartInfo process)
    {
        try
        {
            return Process.Start(process) ?? throw new IOException("Could not start Azure CLI.");
        }
        catch (Win32Exception error)
        {
            throw new IOException("Could not start Azure CLI.", error);
        }
    }

    private static async Task<string> CliTokenAsync(string? subscription, CancellationToken cancellation)
    {
        var process = new ProcessStartInfo
        {
            FileName = OperatingSystem.IsWindows() ? "cmd.exe" : "az",
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
            CreateNoWindow = true
        };
        if (OperatingSystem.IsWindows())
            foreach (var arg in new[] { "/d", "/c", "az" }) process.ArgumentList.Add(arg);
        foreach (var arg in new[] { "account", "get-access-token", "--resource",
            "https://cognitiveservices.azure.com", "--query", "accessToken", "--output", "tsv" })
            process.ArgumentList.Add(arg);
        if (!string.IsNullOrWhiteSpace(subscription))
        {
            if (!Guid.TryParse(subscription, out var id))
                throw new IOException("Foundry subscription must be an Azure subscription GUID.");
            process.ArgumentList.Add("--subscription");
            process.ArgumentList.Add(id.ToString("D"));
        }
        using var child = StartCli(process);
        try
        {
            var stdout = child.StandardOutput.ReadToEndAsync(cancellation);
            var stderr = child.StandardError.ReadToEndAsync(cancellation);
            await child.WaitForExitAsync(cancellation);
            var token = (await stdout).Trim();
            await stderr;
            if (child.ExitCode != 0 || token.Length == 0)
                throw new IOException("Azure CLI token acquisition failed.");
            return token;
        }
        catch (OperationCanceledException)
        {
            if (!child.HasExited) child.Kill(entireProcessTree: true);
            throw;
        }
    }
}





