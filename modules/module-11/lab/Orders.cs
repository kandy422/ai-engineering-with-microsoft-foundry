using System.Text.Json;
using System.Text.RegularExpressions;

static class Orders
{
    public static OrderResult Lookup(Caller caller, OrderInput input)
    {
        if (!Regex.IsMatch(input.OrderId, @"\AORD-[0-9]{4}\z"))
            return new("invalid_argument");

        return (caller.Id, input.OrderId) switch
        {
            ("alex", "ORD-1042") => new("ok", "Shipped"),
            ("sam", "ORD-2045") => new("ok", "Processing"),
            _ => new("not_available")
        };
    }
    public static OrderResult Invoke(Caller caller, string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var args = document.RootElement;
            if (args.ValueKind != JsonValueKind.Object ||
                args.EnumerateObject().Count() != 1 ||
                !args.TryGetProperty("orderId", out var id) ||
                id.ValueKind != JsonValueKind.String)
                return new("invalid_argument");
            return Lookup(caller, new(id.GetString()!));
        }
        catch (JsonException)
        {
            return new("invalid_argument");
        }
    }
}
record Caller(string Id);
record OrderInput(string OrderId);
record OrderResult(string Code, string? Status = null);
