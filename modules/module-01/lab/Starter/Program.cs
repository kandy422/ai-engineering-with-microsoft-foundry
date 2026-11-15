using System.Net.Http.Json;

if (args is ["--check"])
{
    var address = Environment.GetEnvironmentVariable("ORDER_API_BASE");
    if (!Uri.TryCreate(address, UriKind.Absolute, out var baseUri) ||
        baseUri.Scheme != "http" || !baseUri.IsLoopback)
    {
        Console.Error.WriteLine("CONFIG: ORDER_API_BASE must be a loopback HTTP URL");
        return 2;
    }

    using var http = new HttpClient { BaseAddress = baseUri, Timeout = TimeSpan.FromSeconds(5) };
    foreach (var id in new[] { "ORD-1042", "ORD-9999", "" })
    {
        using var response = await http.PostAsJsonAsync("/lookup", new Lookup(id));
        Console.WriteLine($"{id,-8} {(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
    }
    return 0;
}

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();
app.MapPost("/lookup", (Lookup request) =>
{
    if (string.IsNullOrWhiteSpace(request.OrderId))
        return Results.BadRequest(new { error = "orderId is required" });
    return request.OrderId == "ORD-1042"
        ? Results.Ok(new { orderId = request.OrderId, status = "in_transit" })
        : Results.NotFound(new { error = "order not found" });
});
app.Run("http://127.0.0.1:8401");
return 0;

record Lookup(string OrderId);

