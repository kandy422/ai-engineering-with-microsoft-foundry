static class ShippingPolicy
{
    // Module 10's fixture uses document metadata and a single section.
    public static Policy Load(string path)
    {
        var blocks = File.ReadAllLines(path).Where(line => !string.IsNullOrWhiteSpace(line)).ToArray();
        if (blocks.Length != 2)
            throw new FormatException("The shipping fixture requires one metadata line and one passage.");
        var metadata = blocks[0].Split('|', StringSplitOptions.TrimEntries);
        var passage = blocks[1].Split('|', 2, StringSplitOptions.TrimEntries);
        if (metadata.Length != 3 || passage.Length != 2 || metadata[1] != "shipping" ||
            !DateOnly.TryParseExact(metadata[2], "yyyy-MM-dd", out _))
            throw new FormatException("The shipping fixture does not match the Module 10 policy format.");
        return new Policy(metadata[1], $"{metadata[0]}#{passage[0]}", passage[1]);
    }
}
