using System.Text.RegularExpressions;

internal record Evidence(string Id, string Source, string Value, string? NormalizedOrder = null);
internal record Reply(string State, string Text, string[] EvidenceIds);

internal static class Combine
{
    private const string Clarify = "Please confirm your order number in the text field.";

    internal static string? Order(string text)
    {
        // The fictional shop uses exactly six digits. Keep leading zeroes.
        var match = Regex.Match(text, @"\b(?:order\s+)?([0-9]{6})\b", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value : null;
    }

    internal static Reply Resolve(Evidence voice, Evidence receipt, ReceiptDecision check, Evidence policy)
    {
        var order = voice.NormalizedOrder;
        if (order is null || order != receipt.NormalizedOrder)
            return new("clarify", Clarify, [voice.Id, receipt.Id]);
        if (check.State != "validated")
            return new("review", "An agent needs to verify the receipt.", [receipt.Id]);
        return new("answer", $"For order {order}, {policy.Value}", [voice.Id, receipt.Id, policy.Id]);
    }
}
