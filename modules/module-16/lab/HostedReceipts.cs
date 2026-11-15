using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using Azure.Core;

sealed class HostedReceipts(DurableApproval orders, ReceiptAnalyzer analyzer)
{
    internal const int MaxBytes = 100_000;

    internal async Task<IResult> UploadAsync(HttpRequest request, CustomerScope customer, CancellationToken ct)
    {
        var mime = request.ContentType?.ToLowerInvariant();
        if (mime is not ("image/png" or "image/jpeg"))
            return Results.Json(new Reply("unsupported_image"), statusCode: 415);
        if (request.ContentLength > MaxBytes)
            return Results.Json(new Reply("image_too_large"), statusCode: 413);
        if (!request.Headers.TryGetValue("X-Order-Id", out var ids) || ids.Count != 1 ||
            !DurableApproval.ValidOrderId(ids[0]))
            return Results.BadRequest(new Reply("order_required"));

        var image = new byte[MaxBytes + 1];
        var size = await request.Body.ReadAtLeastAsync(image, image.Length, false, ct);
        if (size > MaxBytes)
            return Results.Json(new Reply("image_too_large"), statusCode: 413);
        return Results.Ok(await VerifyAsync(customer, ids[0]!, mime, image.AsMemory(0, size), ct));
    }

    internal async Task<Reply> VerifyAsync(CustomerScope customer, string orderId, string mime, ReadOnlyMemory<byte> image,
        CancellationToken ct)
    {
        if (!IsImage(mime, image.Span)) return new("invalid_receipt");
        var order = await orders.GetOrderAsync(customer, orderId, ct);
        if (order is null) return new("denied");
        var reference = order.GetString("ReceiptReference");
        if (reference is not { Length: 6 } || !reference.All(char.IsAsciiDigit)) return new("receipt_review");

        var extraction = await analyzer.AnalyzeAsync(image, ct);
        if (extraction.Invalid) return new("invalid_receipt");
        if (extraction.Reference is null || extraction.Reference != reference) return new("receipt_review");
        return new("receipt_matched", $"Order {orderId} matches receipt reference {reference}. Current order status: {order.GetString("Status")}.");
    }

    private static bool IsImage(string mime, ReadOnlySpan<byte> image) => mime switch
    {
        "image/png" => image.StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }),
        "image/jpeg" => image.StartsWith(new byte[] { 255, 216, 255 }),
        _ => false
    };
}

record ReceiptExtraction(string? Reference, bool Invalid = false);

sealed partial class ReceiptAnalyzer(HttpClient http, TokenCredential credential, Uri endpoint, TimeProvider clock)
{
    private const string ModelPath = "/documentintelligence/documentModels/prebuilt-receipt";
    private const string ApiVersion = "api-version=2024-11-30";

    internal Task<ReceiptExtraction> AnalyzeAsync(ReadOnlyMemory<byte> image, CancellationToken ct) =>
        HostedTelemetry.DependencyAsync("Document Intelligence receipt", () => AnalyzeCoreAsync(image, ct));

    private async Task<ReceiptExtraction> AnalyzeCoreAsync(ReadOnlyMemory<byte> image, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        var cancellation = timeout.Token;
        var token = await credential.GetTokenAsync(new TokenRequestContext(["https://cognitiveservices.azure.com/.default"]), cancellation);
        using var submit = new HttpRequestMessage(HttpMethod.Post, new Uri(endpoint, $"{ModelPath}:analyze?{ApiVersion}&pages=1"))
        {
            Content = JsonContent.Create(new { base64Source = Convert.ToBase64String(image.Span) })
        };
        submit.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
        using var submitted = await http.SendAsync(submit, cancellation);
        if (submitted.StatusCode == HttpStatusCode.BadRequest) return new(null, Invalid: true);
        submitted.EnsureSuccessStatusCode();
        if (submitted.StatusCode != HttpStatusCode.Accepted ||
            !submitted.Headers.TryGetValues("Operation-Location", out var locations) ||
            !TryGetPollUri(locations.FirstOrDefault(), out var pollUri))
            throw new JsonException("Document Intelligence returned an invalid operation reference.");

        for (var attempt = 0; attempt < 10; attempt++)
        {
            await Task.Delay(TimeSpan.FromSeconds(2), clock, cancellation);
            using var poll = new HttpRequestMessage(HttpMethod.Get, pollUri);
            poll.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
            using var response = await http.SendAsync(poll, cancellation);
            response.EnsureSuccessStatusCode();
            using var data = await response.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken: cancellation)
                ?? throw new JsonException("Document Intelligence returned an empty result.");
            switch (Text(data.RootElement, "status"))
            {
                case "notStarted" or "running": continue;
                case "succeeded": return ParseReceipt(data.RootElement);
                case "failed" when data.RootElement.TryGetProperty("error",out var error) &&
                    Text(error,"code") is "InvalidContent" or "InvalidImage" or "InvalidContentDimensions":
                    return new(null, Invalid: true);
                default: throw new JsonException("Document Intelligence analysis did not succeed.");
            }
        }
        throw new HttpRequestException("Document Intelligence poll budget expired.");
    }

    private bool TryGetPollUri(string? location, out Uri? pollUri)
    {
        pollUri = null;
        const string prefix = ModelPath + "/analyzeResults/";
        if (!Uri.TryCreate(location, UriKind.Absolute, out var uri) ||
            uri.GetLeftPart(UriPartial.Authority) != endpoint.GetLeftPart(UriPartial.Authority) ||
            uri.UserInfo.Length != 0 || !uri.AbsolutePath.StartsWith(prefix, StringComparison.Ordinal) ||
            !Guid.TryParseExact(uri.AbsolutePath[prefix.Length..], "D", out var operation))
            return false;
        pollUri = new Uri(endpoint, $"{prefix}{operation:D}?{ApiVersion}");
        return true;
    }

    internal static ReceiptExtraction ParseReceipt(JsonElement response)
    {
        if (response.ValueKind != JsonValueKind.Object || !response.TryGetProperty("analyzeResult",out var result) ||
            Text(result,"modelId") != "prebuilt-receipt" ||
            !result.TryGetProperty("documents",out var documents) ||
            documents.ValueKind != JsonValueKind.Array || documents.GetArrayLength() != 1 ||
            Text(documents[0],"docType")?.StartsWith("receipt.",StringComparison.Ordinal) != true ||
            !result.TryGetProperty("pages",out var pages) ||
            pages.ValueKind != JsonValueKind.Array || pages.GetArrayLength() != 1 ||
            pages[0].ValueKind != JsonValueKind.Object ||
            !pages[0].TryGetProperty("lines",out var lines) || lines.ValueKind != JsonValueKind.Array)
            return new(null, Invalid: true);
        var matches = lines.EnumerateArray()
            .Select(line => OrderLine().Match(Text(line,"content")?.Trim() ?? ""))
            .Where(match => match.Success).Take(2).ToArray();
        return new(matches.Length == 1 ? matches[0].Groups[1].Value : null);
    }

    private static string? Text(JsonElement element,string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property,out var value) &&
        value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    [GeneratedRegex(@"\AORDER[ ]+([0-9]{6})\z", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex OrderLine();
}
