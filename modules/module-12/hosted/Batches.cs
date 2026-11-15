using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

// The capstone supplies this canonical record after an authenticated, customer-scoped lookup.
internal sealed record CanonicalOrder(string Tenant, string Customer, string Id, string ReceiptReference);

internal static partial class EvidenceGate
{
    [GeneratedRegex(@"(?m)^\s*ORDER[ \t]+([0-9]{6})[ \t\r]*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ReceiptOrder();

    [GeneratedRegex(@"\b(?:(?:order(?: number)?(?: is)?)[ :#-]*)?([0-9]{6})\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex VoiceOrder();

    internal static string Decide(CanonicalOrder? order, string tenant, string customer, string speech,
        string receiptText, ReceiptDecision validation)
    {
        if (order is null || order.Tenant != tenant || order.Customer != customer)
            return "denied";
        var spoken = VoiceOrder().Matches(speech).Select(match => match.Groups[1].Value).Distinct().ToArray();
        var printed = ReceiptOrder().Matches(receiptText).Select(match => match.Groups[1].Value).Distinct().ToArray();
        if (spoken.Length != 1 || printed.Length != 1 || spoken[0] != order.ReceiptReference ||
            printed[0] != order.ReceiptReference)
            return "clarify";
        return validation.State == "validated" ? "eligible-for-policy-reply" : "review";
    }

    internal static ReceiptDecision ValidateDocument(JsonNode? fields)
    {
        // Demonstration threshold, to be calibrated with representative labelled receipts.
        JsonNode? Field(string name) => fields?[name] is { } field &&
            (field["confidence"]?.GetValue<double>() ?? 0) >= .8 ? field : null;
        decimal? Amount(string name) => Field(name)?["valueCurrency"]?["amount"]?.GetValue<decimal>();
        var date = Field("TransactionDate")?["valueDate"]?.GetValue<string>();
        var parsed = DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var value);
        var currencies = new[] { "Subtotal", "TotalTax", "Total" }
            .Select(name => Field(name)?["valueCurrency"]?["currencyCode"]?.GetValue<string>())
            .Distinct().ToArray();
        if (currencies.Length != 1)
            return new("review", "currency mismatch or incomplete currency evidence");
        return Receipts.Validate(new(parsed ? value : null, Amount("Subtotal"), Amount("TotalTax"), Amount("Total")),
            new(2026, 11, 15));
    }
}
