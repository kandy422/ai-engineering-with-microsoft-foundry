using System.Text.Json;

internal static class FoundryRecording
{
    public static object Read(string path)
    {
        using var response = JsonDocument.Parse(File.ReadAllText(path));
        var root = response.RootElement;
        var choice = root.GetProperty("choices")[0];
        var content = choice.GetProperty("message").GetProperty("content").GetString() ?? "";
        return new
        {
            content,
            finishReason = choice.GetProperty("finish_reason").GetString(),
            usage = root.GetProperty("usage").Clone(),
            scheduleValid = ScheduleValid(content),
        };
    }

    private static bool ScheduleValid(string content)
    {
        try
        {
            using var json = JsonDocument.Parse(content);
            var answer = json.RootElement;
            if (answer.ValueKind != JsonValueKind.Object || answer.EnumerateObject().Count() != 3 ||
                !TrySlot(answer, "Ava", out var ava) || !TrySlot(answer, "Ben", out var ben) ||
                !TrySlot(answer, "Chen", out var chen) ||
                new HashSet<string> { ava, ben, chen }.Count != 3)
                return false;
            return ava != "09" &&
                   string.CompareOrdinal(ben, chen) < 0 && chen != "11";
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TrySlot(JsonElement answer, string name, out string slot)
    {
        slot = "";
        if (!answer.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
            return false;
        slot = value.GetString() ?? "";
        return slot is "09" or "10" or "11";
    }
}
