using System.Diagnostics;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

return await HostedLab.Run(args);

internal static partial class HostedLab
{
    private const string Clarification = "I could not verify your order number. Please enter it in the text field.";
    private static readonly string Module = Directory.GetCurrentDirectory();
    private static readonly HttpClient Http = new(new HttpClientHandler { AllowAutoRedirect = false })
    {
        Timeout = TimeSpan.FromSeconds(20)
    };
    private static string output = "";
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    internal static async Task<int> Run(string[] args)
    {
        try
        {
            if (args is ["check"]) { Check(); return 0; }
            if (args is not [_, _] and not ["voice-retry", _, _])
                throw new InvalidOperationException("Use check, or vision | documents | speech | voice | combine followed by a new output directory. Voice retry takes the previous evidence directory as a third argument.");
            var mode = args[0];
            output = Path.GetFullPath(args[1]);
            if (Directory.Exists(output) || File.Exists(output))
                throw new InvalidOperationException("Use a new evidence directory for each allocated run.");
            Directory.CreateDirectory(output);
            using (File.Open(Path.Combine(output, "run.started"), FileMode.CreateNew)) { }
            switch (mode)
            {
                case "vision":
                    await Vision("thumbnail", Asset("public/damage-low.png"));
                    await Vision("detail", Asset("public/damage-detail.png"));
                    break;
                case "documents":
                    await Document("receipt", Asset("lab/evidence/receipt.png"));
                    await Document("mismatch", Asset("lab/evidence/receipt-mismatch.png"));
                    await Document("layout", Asset("lab/evidence/receipt.png"), "prebuilt-layout");
                    break;
                case "speech":
                    await Recognize("clean", Asset("hosted/fixtures/request-clean.wav"));
                    await Recognize("noisy", Asset("hosted/fixtures/request-noisy.wav"));
                    break;
                case "voice":
                    var heard = await Recognize("prerecorded-fictional", Asset("hosted/fixtures/request-unclear.wav"));
                    if (heard.Length == 0 || System.Text.RegularExpressions.Regex.IsMatch(heard, @"\b[0-9]{6}\b"))
                        throw new InvalidOperationException("The clarification checkpoint requires recognized speech with an incomplete reference.");
                    Save("turn.json", new { heard, reply = Clarification, next = "text-fallback" });
                    await PlayReply(await Synthesize());
                    break;
                case "voice-retry":
                    if (args.Length != 3)
                        throw new InvalidOperationException("Voice retry requires the previous recognition evidence directory.");
                    var prior = Path.GetFullPath(args[2]);
                    var receiptJson = JsonNode.Parse(File.ReadAllBytes(Path.Combine(prior, "prerecorded-fictional.http.json")));
                    var recognitionPath = Path.Combine(prior, "prerecorded-fictional.response.json");
                    var recognitionBytes = File.ReadAllBytes(recognitionPath);
                    if (receiptJson?["status"]?.GetValue<int>() != 200 ||
                        receiptJson["responseSha256"]?.GetValue<string>() != Convert.ToHexString(SHA256.HashData(recognitionBytes)))
                        throw new InvalidOperationException("The prior recognition receipt cannot be verified.");
                    var priorInput = JsonNode.Parse(File.ReadAllBytes(Path.Combine(prior, "prerecorded-fictional.input.json")));
                    var fixtureHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Asset("hosted/fixtures/request-unclear.wav"))));
                    if (priorInput?["sha256"]?.GetValue<string>() != fixtureHash)
                        throw new InvalidOperationException("The prior recognition was not for the approved fictional WAV.");
                    var recognition = JsonNode.Parse(recognitionBytes);
                    var verifiedText = recognition?["DisplayText"]?.GetValue<string>() ??
                        recognition?["NBest"]?[0]?["Display"]?.GetValue<string>() ?? "";
                    if (recognition?["RecognitionStatus"]?.GetValue<string>() != "Success" || verifiedText.Length == 0 ||
                        System.Text.RegularExpressions.Regex.IsMatch(verifiedText, @"\b[0-9]{6}\b"))
                        throw new InvalidOperationException("The previous recognition cannot support the clarification checkpoint.");
                    Save("turn.json", new { heard = verifiedText, reply = Clarification, next = "text-fallback",
                        priorRecognitionResponseSha256 = receiptJson["responseSha256"]?.GetValue<string>() });
                    await PlayReply(await Synthesize());
                    break;
                case "combine":
                    var clean = await Recognize("agreement", Asset("hosted/fixtures/request-agreement.wav"));
                    var changed = await Recognize("conflict", Asset("hosted/fixtures/request-conflict.wav"));
                    var receipt = await Document("receipt", Asset("lab/evidence/receipt.png"));
                    // An explicit lab-only binding. Production must supply the canonical record after authorization.
                    var order = new CanonicalOrder("lab-tenant", "customer-a", "ORD-1042", "048271");
                    var agreement = EvidenceGate.Decide(order, order.Tenant, order.Customer, clean, receipt.Text, receipt.Decision);
                    var conflict = EvidenceGate.Decide(order, order.Tenant, order.Customer, changed, receipt.Text, receipt.Decision);
                    Save("decisions.json", new { scope = "lab canonical order and local policy fixture", agreement, conflict,
                        reply = agreement == "eligible-for-policy-reply" ?
                            File.ReadLines(Asset("lab/policy.txt")).Single(line => line.StartsWith("DAMAGED-01|")) : null,
                        evidence = new { voice = "agreement.response.json", changedVoice = "conflict.response.json",
                            receipt = "receipt.result.response.json", policy = "DAMAGED-01" } });
                    break;
                default: throw new InvalidOperationException("Unknown recording mode.");
            }
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error.Message); return 1; }
    }

    private static string Asset(string file) => Path.Combine(Module, file);
    private static string Setting(string name) => Environment.GetEnvironmentVariable(name) is { Length: > 0 } value
        ? value : throw new InvalidOperationException($"Configure {name} before running the allocated capture.");

    private static Uri Origin(string setting, params string[] domains)
    {
        var uri = new Uri(Setting(setting));
        if (uri.Scheme != "https" || !uri.IsDefaultPort || uri.UserInfo.Length != 0 ||
            uri.PathAndQuery != "/" || uri.Fragment.Length != 0 ||
            !domains.Any(domain => uri.Host.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"{setting} must be the assigned HTTPS resource origin.");
        return uri;
    }

    private static void Save(string name, object data)
    {
        var json = JsonSerializer.Serialize(data, Indented);
        File.WriteAllText(Path.Combine(output, name), json + Environment.NewLine);
        if (name.EndsWith(".decision.json") || name is "decisions.json" or "turn.json" or "fallback.json")
            Console.WriteLine(json);
    }

    private static async Task<(byte[] Body, Uri? Operation)> Send(string id, HttpMethod method, Uri url,
        string credentialHeader, string credential, HttpContent? content = null, string? audioFormat = null)
    {
        using var request = new HttpRequestMessage(method, url) { Content = content };
        request.Headers.Add(credentialHeader, credential);
        if (audioFormat is not null)
        {
            request.Headers.Add("X-Microsoft-OutputFormat", audioFormat);
            request.Headers.UserAgent.ParseAdd("PixelProtocol-FoundryCourse/1.0");
        }
        request.Headers.Accept.Add(new(audioFormat is null ? "application/json" : "audio/wav"));
        var startedUtc = DateTimeOffset.UtcNow;
        var start = Stopwatch.StartNew();
        using var response = await Http.SendAsync(request);
        var body = await response.Content.ReadAsByteArrayAsync();
        var isAudio = audioFormat is not null && response.IsSuccessStatusCode;
        var headers = response.Headers.Where(pair => pair.Key.ToLowerInvariant() is
            "apim-request-id" or "x-request-id" or "request-id" or "x-ms-request-id" or "retry-after")
            .ToDictionary(pair => pair.Key, pair => string.Join(",", pair.Value));
        Save(id + ".http.json", new { startedUtc, method = method.Method, endpoint = url.GetLeftPart(UriPartial.Path),
            status = (int)response.StatusCode, elapsedMs = start.ElapsedMilliseconds, headers,
            responseSha256 = Convert.ToHexString(SHA256.HashData(body)) });
        if (!isAudio) File.WriteAllBytes(Path.Combine(output, id + ".response.json"), body);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"{id} returned HTTP {(int)response.StatusCode}. Inspect the saved response before allocating a retry.");
        var operation = response.Headers.TryGetValues("Operation-Location", out var locations)
            ? new Uri(locations.Single(), UriKind.Absolute) : null;
        return (body, operation);
    }

    private static byte[] Fixture(string id, string path, int maxBytes)
    {
        if (new FileInfo(path).Length > maxBytes) throw new InvalidDataException("Fixture exceeds the input-byte bound.");
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length > maxBytes) throw new InvalidDataException("Fixture exceeds the input-byte bound.");
        Save(id + ".input.json", new { file = Path.GetFileName(path), bytes = bytes.Length,
            sha256 = Convert.ToHexString(SHA256.HashData(bytes)) });
        return bytes;
    }

    private static HttpContent Payload(byte[] bytes, string type)
    {
        var content = new ByteArrayContent(bytes);
        // Speech documents an unquoted slash inside its codec parameter.
        content.Headers.TryAddWithoutValidation("Content-Type", type);
        return content;
    }

    private static async Task Vision(string id, string path)
    {
        const string prompt = "Describe visible damage only. Name the visible location if supported; otherwise say uncertain. Do not infer cause, safety, or warranty. Answer in two short sentences.";
        var uri = new Uri(Origin("M12_VISION_ORIGIN", "openai.azure.com"), "/openai/v1/chat/completions");
        var model = Setting("M12_VISION_DEPLOYMENT");
        var dataUrl = "data:image/png;base64," + Convert.ToBase64String(Fixture(id, path, 100_000));
        var payload = JsonSerializer.SerializeToUtf8Bytes(new { model, max_completion_tokens = 512,
            messages = new[] { new { role = "user", content = new object[] {
                new { type = "text", text = prompt }, new { type = "image_url", image_url = new { url = dataUrl } }
            } } } });
        Save(id + ".request.json", new { model, prompt, max_completion_tokens = 512 });
        var (body, _) = await Send(id, HttpMethod.Post, uri, "Authorization",
            "Bearer " + Setting("M12_VISION_TOKEN"), Payload(payload, "application/json"));
        var response = JsonNode.Parse(body);
        var choice = response?["choices"]?[0];
        Save(id + ".decision.json", new { model = response?["model"], text = choice?["message"]?["content"],
            finishReason = choice?["finish_reason"], usage = response?["usage"] });
    }

    private static async Task<(string Text, ReceiptDecision Decision)> Document(string id, string path,
        string model = "prebuilt-receipt")
    {
        var origin = Origin("M12_DOCUMENT_ORIGIN", "cognitiveservices.azure.com");
        var uri = new Uri(origin, $"/documentintelligence/documentModels/{model}:analyze?api-version=2024-11-30&pages=1");
        var authorization = "Bearer " + Setting("M12_AZURE_TOKEN");
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(new
        {
            base64Source = Convert.ToBase64String(Fixture(id, path, 100_000))
        });
        var (_, operation) = await Send(id, HttpMethod.Post, uri, "Authorization", authorization,
            Payload(payload, "application/json"));
        if (operation is null || operation.GetLeftPart(UriPartial.Authority) != origin.GetLeftPart(UriPartial.Authority) ||
            operation.UserInfo.Length != 0 ||
            !operation.AbsolutePath.StartsWith($"/documentintelligence/documentModels/{model}/analyzeResults/", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Unexpected Document Intelligence operation URL.");

        for (var attempt = 1; attempt <= 10; attempt++)
        {
            await Task.Delay(TimeSpan.FromSeconds(3));
            var (body, _) = await Send(id + ".poll-" + attempt, HttpMethod.Get, operation, "Authorization", authorization);
            var result = JsonNode.Parse(body);
            switch (result?["status"]?.GetValue<string>())
            {
                case "notStarted" or "running": continue;
                case "succeeded":
                    File.WriteAllBytes(Path.Combine(output, id + ".result.response.json"), body);
                    var analysis = result["analyzeResult"];
                    var text = analysis?["content"]?.GetValue<string>() ?? "";
                    if (model == "prebuilt-layout")
                    {
                        var tables = analysis?["tables"];
                        var tableCount = tables is JsonArray tableArray ? tableArray.Count : 0;
                        Save(id + ".decision.json", new { tableCount, tables, lines = analysis?["pages"]?[0]?["lines"] });
                        return (text, new("layout-only", "inspect actual lines and tables"));
                    }
                    var fields = analysis?["documents"] is JsonArray { Count: 1 } docs ? docs[0]?["fields"] : null;
                    var decision = EvidenceGate.ValidateDocument(fields);
                    Save(id + ".decision.json", new { decision, fields });
                    return (text, decision);
                default: throw new InvalidOperationException("Document analysis did not succeed. Inspect saved poll evidence.");
            }
        }
        throw new TimeoutException("Document poll budget exhausted. Do not resubmit automatically.");
    }

    private static async Task<string> Recognize(string id, string path)
    {
        var input = Fixture(id, path, 321_024);
        var seconds = WavSeconds(input);
        var uri = new Uri(Origin("M12_SPEECH_ORIGIN", "cognitiveservices.azure.com"),
            "/stt/speech/recognition/conversation/cognitiveservices/v1?language=en-GB&format=detailed");
        var (body, _) = await Send(id, HttpMethod.Post, uri, "Authorization", SpeechAuthorization(),
            Payload(input, "audio/wav; codecs=audio/pcm; samplerate=16000"));
        var response = JsonNode.Parse(body);
        var state = response?["RecognitionStatus"]?.GetValue<string>();
        var heard = state == "Success" ? response?["DisplayText"]?.GetValue<string>() ??
            response?["NBest"]?[0]?["Display"]?.GetValue<string>() ?? "" : "";
        Save(id + ".decision.json", new { seconds, state, heard });
        return heard;
    }

    private static async Task<string> Synthesize()
    {
        const string voice = "en-GB-SoniaNeural";
        var ssml = $"<speak version='1.0' xmlns='http://www.w3.org/2001/10/synthesis' xml:lang='en-GB'><voice name='{voice}'>{Clarification}</voice></speak>";
        Save("synthesis.request.json", new { voice, text = Clarification, characters = Clarification.Length });
        var uri = new Uri(Origin("M12_SPEECH_ORIGIN", "cognitiveservices.azure.com"), "/tts/cognitiveservices/v1");
        var (body, _) = await Send("synthesis", HttpMethod.Post, uri, "Authorization",
            SpeechAuthorization(), Payload(Encoding.UTF8.GetBytes(ssml), "application/ssml+xml"), "riff-16khz-16bit-mono-pcm");
        WavSeconds(body);
        var path = Path.Combine(output, "reply.wav");
        File.WriteAllBytes(path, body);
        return path;
    }

    private static async Task PlayReply(string wav)
    {
        var scriptedStop = Environment.GetEnvironmentVariable("M12_VOICE_STOP_MS");
        int? stopAfterMs = int.TryParse(scriptedStop, out int stopMs) && stopMs is >= 500 and <= 2000 ? stopMs : null;
        if (!OperatingSystem.IsWindows() || (Console.IsInputRedirected && stopAfterMs is null))
            throw new InvalidOperationException("Use Windows with speaker output and an interactive terminal or M12_VOICE_STOP_MS between 500 and 2000.");
        var duration = WavSeconds(File.ReadAllBytes(wav));
        if (stopAfterMs >= duration * 1000)
            throw new InvalidOperationException("The scripted stop must occur before playback ends.");
        Console.WriteLine(stopAfterMs is null
            ? "Press Space to stop actual speaker playback. Then enter the six-digit receipt reference."
            : $"The application will request an explicit stop at {stopAfterMs} ms; then enter the six-digit receipt reference.");
        var playback = Stopwatch.StartNew();
        if (!PlaySound(wav, IntPtr.Zero, 0x00020003))
            throw new InvalidOperationException("Windows could not start playback.");
        long? stopRequestedMs = null;
        var stopSucceeded = false;
        var startedUtc = DateTimeOffset.UtcNow;
        Save("playback.json", new { startedUtc, sourceSeconds = duration, startCallReturned = true,
            trigger = stopAfterMs is null ? "space-key" : "scripted-timer", stopAfterMs });
        try
        {
            while (playback.Elapsed < TimeSpan.FromSeconds(duration))
            {
                var triggered = stopAfterMs is int time ? playback.ElapsedMilliseconds >= time
                    : Console.KeyAvailable && Console.ReadKey(intercept: true).Key == ConsoleKey.Spacebar;
                if (triggered)
                {
                    stopRequestedMs = playback.ElapsedMilliseconds;
                    break;
                }
                await Task.Delay(20);
            }
        }
        finally
        {
            stopSucceeded = PlaySound(null, IntPtr.Zero, 0);
            if (stopRequestedMs is not null)
                Save("interruption.json", new { stopRequestedMs, stopCallReturned = stopSucceeded });
        }
        var typed = Console.ReadLine() == "048271";
        Save("fallback.json", new { exactFixtureMatch = typed, scope = "fixture reference only" });
        if (!typed || stopRequestedMs is null || !stopSucceeded)
            throw new InvalidOperationException("Interruption or typed fixture recovery was not observed.");
    }

    private static string SpeechAuthorization()
    {
        var resourceId = Setting("M12_SPEECH_RESOURCE_ID");
        if (!resourceId.StartsWith("/subscriptions/", StringComparison.OrdinalIgnoreCase) ||
            !resourceId.Contains("/providers/Microsoft.CognitiveServices/accounts/", StringComparison.OrdinalIgnoreCase) ||
            resourceId.Contains('#'))
            throw new InvalidOperationException("Use the allocated Speech ARM resource ID.");
        return $"Bearer aad#{resourceId}#{Setting("M12_AZURE_TOKEN")}";
    }

    private static double WavSeconds(byte[] bytes)
    {
        using var reader = new BinaryReader(new MemoryStream(bytes));
        string Tag() => Encoding.ASCII.GetString(reader.ReadBytes(4));
        if (Tag() != "RIFF") throw new InvalidDataException("Expected RIFF.");
        reader.ReadUInt32();
        if (Tag() != "WAVE") throw new InvalidDataException("Expected WAVE.");
        var hasFormat = false;
        while (reader.BaseStream.Position + 8 <= bytes.Length)
        {
            var tag = Tag();
            var length = reader.ReadUInt32();
            var start = reader.BaseStream.Position;
            if (start + length > bytes.Length) break;
            if (tag == "fmt " && length >= 16)
            {
                var format = reader.ReadUInt16();
                var channels = reader.ReadUInt16();
                var rate = reader.ReadUInt32();
                reader.ReadUInt32(); reader.ReadUInt16();
                hasFormat = format == 1 && channels == 1 && rate == 16000 && reader.ReadUInt16() == 16;
            }
            if (tag == "data" && hasFormat && length is > 0 and <= 320_000 && length % 2 == 0)
                return length / 32000.0;
            reader.BaseStream.Position = start + length + length % 2;
        }
        throw new InvalidDataException("Use mono 16 kHz PCM16 WAV with at most ten seconds of audio.");
    }

    private static void Check()
    {
        foreach (var name in new[] { "clean", "noisy", "agreement", "conflict", "unclear" })
            Console.WriteLine($"{name}: {WavSeconds(File.ReadAllBytes(Asset($"hosted/fixtures/request-{name}.wav"))):F3}s");
        var order = new CanonicalOrder("lab-tenant", "customer-a", "ORD-1042", "048271");
        ReceiptDecision valid = new("validated", "");
        void Require(string actual, string expected)
        {
            if (actual != expected) throw new InvalidOperationException($"Expected {expected}, got {actual}.");
        }
        string Gate(string speech, string receipt, string customer = "customer-a", ReceiptDecision? decision = null) =>
            EvidenceGate.Decide(order, "lab-tenant", customer, speech, receipt, decision ?? valid);
        Require(Gate("Order 048271", "ORDER 048271"), "eligible-for-policy-reply");
        Require(Gate("Order 048272", "ORDER 048271"), "clarify");
        Require(Gate("Order 048271 or 048272", "ORDER 048271"), "clarify");
        Require(Gate("Order 048271", "ORDER 048272"), "clarify");
        Require(Gate("Order 048271", "DATE 2026-11-02"), "clarify");
        Require(Gate("Order 048271", "ORDER 048271", "customer-b"), "denied");
        Require(Gate("Order 048271", "ORDER 048271", decision: new("review", "")), "review");
        Require(EvidenceGate.ValidateDocument(null).State, "review");
        var fields = JsonNode.Parse("""
          {
            "TransactionDate": { "confidence": 0.99, "valueDate": "2026-11-02" },
            "Subtotal": { "confidence": 0.99, "valueCurrency": { "amount": 40, "currencyCode": "USD" } },
            "TotalTax": { "confidence": 0.99, "valueCurrency": { "amount": 4, "currencyCode": "USD" } },
            "Total": { "confidence": 0.99, "valueCurrency": { "amount": 44, "currencyCode": "USD" } }
          }
          """)!;
        Require(EvidenceGate.ValidateDocument(fields).State, "validated");
        fields["Total"]!["valueCurrency"]!["amount"] = 48m;
        Require(EvidenceGate.ValidateDocument(fields).Reason, "total does not reconcile");
        fields["Total"]!["confidence"] = .4;
        Require(EvidenceGate.ValidateDocument(fields).State, "review");
        Console.WriteLine("11 local evidence-gate checks passed. No service requests made.");
    }

    [LibraryImport("winmm.dll", EntryPoint = "PlaySoundW", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PlaySound(string? sound, IntPtr module, uint flags);
}
