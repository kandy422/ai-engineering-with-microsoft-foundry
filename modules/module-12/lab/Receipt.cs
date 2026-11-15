using System.Globalization;
using System.Text.RegularExpressions;

internal record Receipt(DateOnly? Date, decimal? Subtotal, decimal? Tax, decimal? Total);
internal record ReceiptDecision(string State, string Reason);

internal static class Receipts
{
    internal static Receipt Parse(string text)
    {
        var dateText = Regex.Match(text, @"\b\d{4}-\d{2}-\d{2}\b").Value;
        DateOnly? date = DateOnly.TryParseExact(dateText, "yyyy-MM-dd",
            CultureInfo.InvariantCulture, DateTimeStyles.None, out var value) ? value : null;
        return new(date, Amount(text, "SUBTOTAL"), Amount(text, "TAX"), Amount(text, "TOTAL"));
    }

    private static decimal? Amount(string text, string name)
    {
        var match = Regex.Match(text, $@"(?m)^{name}[ \t]+(\d+\.\d{{2}})[ \t\r]*$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return decimal.TryParse(match.Groups[1].Value, NumberStyles.Number,
            CultureInfo.InvariantCulture, out var amount) ? amount : null;
    }

    internal static ReceiptDecision Validate(Receipt receipt, DateOnly asOf)
    {
        if (receipt is not { Date: { } date, Subtotal: { } subtotal, Tax: { } tax, Total: { } total })
            return new("review", "missing or unreadable field");
        if (date > asOf) return new("review", "purchase date is in the future");
        if (subtotal + tax != total) return new("review", "total does not reconcile");
        return new("validated", "required fields and arithmetic passed");
    }
}
