using System.Net.Http.Json;
using System.Text.Json;

internal static class Vision
{
    internal const string Prompt = "Describe only visible features of this image in at most two short sentences. Identify any mark on the handle only if you can see it. Do not infer its cause.";

    internal static async Task CompareAsync(string baseUrl, string low, string detail, string outputFolder)
    {
        using var client = new HttpClient { BaseAddress = new Uri(baseUrl), Timeout = TimeSpan.FromMinutes(3) };
        foreach (var (name, path) in new[]{("low",low),("detail",detail)})
        {
            var png = Convert.ToBase64String(await File.ReadAllBytesAsync(path));
            // llama-server's local adapter uses the Chat Completions content shape.
            object[] localContent =
            [
                new {type = "text", text = Prompt},
                new {type = "image_url", image_url = new {url = $"data:image/png;base64,{png}"}}
            ];
            var payload = new {model="smolvlm-500m", messages=new[]{new {role="user",content=localContent}},
                               temperature=0, seed=48271, max_tokens=110};
            using var response = await client.PostAsJsonAsync("v1/chat/completions",payload);
            response.EnsureSuccessStatusCode();
            var raw = await response.Content.ReadAsStringAsync();
            await File.WriteAllTextAsync(Path.Combine(outputFolder,$"12.2-{name}.json"),raw);
            using var json = JsonDocument.Parse(raw);
            var answer = json.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();
            if (string.IsNullOrWhiteSpace(answer)) throw new InvalidDataException("Empty vision response.");
            await File.WriteAllLinesAsync(Path.Combine(outputFolder,$"12.2-{name}.txt"),Wrap(answer,84));
            Console.WriteLine($"{name}: {answer}");
        }
    }

    private static IEnumerable<string> Wrap(string text, int width)
    {
        var line = "";
        foreach (var word in text.Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries))
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
