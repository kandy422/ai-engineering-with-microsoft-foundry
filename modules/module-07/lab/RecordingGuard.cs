using System.Text.Json;
using System.Text.Json.Nodes;

internal sealed class RecordingGuard(string planPath, string evidenceDirectory) :
    DelegatingHandler(new HttpClientHandler { AllowAutoRedirect = false })
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellation)
    {
        var plan = JsonNode.Parse(await File.ReadAllTextAsync(planPath, cancellation))!;
        var expected = plan["request"]!["expectedOutboundBody"];
        var body = JsonNode.Parse(await request.Content!.ReadAsStringAsync(cancellation));
        if (request.Method != HttpMethod.Post || !JsonNode.DeepEquals(expected, body))
            throw new IOException("The outgoing request differs from the reserved recording body.");

        // CreateNew is the durable single-attempt gate, including on process restart.
        await using (var file = new FileStream(Path.Combine(evidenceDirectory, "outbound.json"), FileMode.CreateNew))
        {
            await JsonSerializer.SerializeAsync(file, new
            {
                endpoint = request.RequestUri!.GetLeftPart(UriPartial.Path),
                transport = "C# ASP.NET application, HttpClient, Microsoft Entra ID",
                body,
                startedAtUtc = DateTimeOffset.UtcNow
            }, cancellationToken: cancellation);
        }

        var response = await base.SendAsync(request, cancellation);
        var ids = new Dictionary<string, string>();
        foreach (var name in new[] { "apim-request-id", "x-request-id", "x-ms-request-id" })
            if (response.Headers.TryGetValues(name, out var values))
                ids[name] = string.Join(", ", values);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(evidenceDirectory, "service-headers.json"),
                JsonSerializer.Serialize(new { httpStatus = (int)response.StatusCode, serviceRequestIds = ids }));
            return response;
        }
        catch
        {
            response.Dispose();
            throw;
        }
    }
}
