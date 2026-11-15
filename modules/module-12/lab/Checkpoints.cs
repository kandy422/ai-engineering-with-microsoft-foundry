using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

internal static class Checkpoints
{
    private static readonly DateOnly DemoDate = new(2026, 11, 12);

    internal static void ReceiptChecks(string folder)
    {
        foreach (var name in new[]{"receipt","receipt-mismatch","receipt-missing","receipt-future"})
        {
            var receipt = Receipts.Parse(File.ReadAllText(Path.Combine(folder, name+".txt")));
            var check = Receipts.Validate(receipt, DemoDate);
            var date = receipt.Date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "missing";
            Console.WriteLine($"{name} | {date} | {receipt.Total?.ToString("F2", CultureInfo.InvariantCulture) ?? "missing"} | {check.State}");
            Console.WriteLine(check.Reason);
        }
    }

    internal static void VoiceChecks(string folder)
    {
        var pcm = VoiceLoop.ReadPcm(Path.Combine(folder,"clarification.wav"));
        using var playback = new MemoryStream();
        using var bargeIn = new CancellationTokenSource();
        var blocks = VoiceLoop.Play(playback, pcm, bargeIn.Token,
            count => { if (count == 6) bargeIn.Cancel(); });
        Console.WriteLine($"PCM sink | {blocks} blocks | {playback.Length} bytes | cancelled");
        Console.WriteLine($"Heard 04827 | {(Combine.Order("04827") is null ? "clarify" : "accepted")}");
        Console.WriteLine($"Text 048271 | accepted order {Combine.Order("048271")}");
    }

    internal static void IntegrationChecks(string folder, string policyPath, string? voicePath = null)
    {
        var receiptText = File.ReadAllText(Path.Combine(folder,"receipt.txt"));
        var check = Receipts.Validate(Receipts.Parse(receiptText),DemoDate);
        var orderLine = receiptText.Split('\n').Single(line => line.StartsWith("ORDER ",StringComparison.Ordinal));
        var policy = File.ReadLines(policyPath).Single(line => line.StartsWith("DAMAGED-01|",StringComparison.Ordinal)).Split('|',2);
        var receipt = new Evidence("receipt:order", "Windows OCR · receipt.png", orderLine.Trim(), Combine.Order(orderLine));
        var retrieved = new Evidence(policy[0], "local policy.txt · damage intent", policy[1]);
        var trace = new List<object>();
        (string id, string transcript)[] cases = voicePath is null ? [
            ("clean", File.ReadAllText(Path.Combine(folder,"transcript-clean.txt"))),
            ("conflict", "The handle is cracked. The order is 048272.")
        ] : [(Path.GetFileNameWithoutExtension(voicePath),File.ReadAllText(voicePath))];
        foreach (var (id, transcript) in cases)
        {
            var source = voicePath ?? (id == "clean" ? "local ASR · request-clean.wav" : "conflict text fixture");
            var voice = new Evidence("voice:"+id, source, transcript);
            var normalized = NormalizeSpokenOrder(voice);
            var reply = Combine.Resolve(normalized, receipt, check, retrieved);
            Console.WriteLine($"{id} | voice {normalized.NormalizedOrder ?? "unresolved"} | receipt {receipt.NormalizedOrder ?? "unresolved"} | {reply.State}");
            foreach (var line in Wrap(reply.Text, 79)) Console.WriteLine(line);
            Console.WriteLine(string.Join("  ", reply.EvidenceIds));
            trace.Add(new {id, voice = normalized, receipt, receiptCheck = check, policy = retrieved, reply});
        }
        var traceName = voicePath is null ? "integration-trace.json" : $"{Path.GetFileNameWithoutExtension(voicePath)}-trace.json";
        File.WriteAllText(Path.Combine(folder,traceName),
            JsonSerializer.Serialize(trace, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));
    }

    private static Evidence NormalizeSpokenOrder(Evidence input)
    {
        // Only normalise a complete six-digit run; ask when the evidence is partial.
        var words = new Dictionary<string,char>{
            ["zero"]='0',["oh"]='0',["one"]='1',["two"]='2',["three"]='3',["four"]='4',
            ["five"]='5',["six"]='6',["seven"]='7',["eight"]='8',["nine"]='9'
        };
        var phrase = Regex.Match(input.Value, @"\border\s+(?:number\s+)?(?:is\s+)?([^.!?]+)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!phrase.Success) return input;
        var tokens = Regex.Matches(phrase.Groups[1].Value.ToLowerInvariant(), @"\b[a-z0-9]+\b");
        var digits = new List<char>();
        foreach (System.Text.RegularExpressions.Match token in tokens)
        {
            if (words.TryGetValue(token.Value,out var digit)) digits.Add(digit);
            else if (token.Value.Length > 0 && token.Value.All(char.IsAsciiDigit)) digits.AddRange(token.Value);
            else if (digits.Count == 6) break;
            else digits.Clear();
        }
        return digits.Count == 6 ? input with {NormalizedOrder = new string(digits.ToArray())} : input;
    }

    private static IEnumerable<string> Wrap(string text, int width)
    {
        var line = "";
        foreach (var word in text.Split(' '))
        {
            if (line.Length + 1 + word.Length > width)
            {
                yield return line;
                line = "";
            }
            line = line.Length == 0 ? word : $"{line} {word}";
        }
        if (line.Length > 0) yield return line;
    }
}
