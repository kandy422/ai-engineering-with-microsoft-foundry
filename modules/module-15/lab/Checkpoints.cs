using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

static class Checkpoints
{
    public static Task Run(string id, string? variant = null) => (id, variant) switch
    {
        ("15.2", null) => Provision(),
        ("15.3", null) => Identity(),
        ("15.4", null) => Release(),
        ("15.5", null) => Observe(240),
        ("15.5", "fast") => Observe(30),
        ("15.6", null) => Load(3),
        ("15.6", "two-slots") => Load(2),
        ("15.7", null) => Optimize(),
        ("15.8", null) => Migrate(),
        _ => throw new ArgumentException($"Unknown checkpoint: {id}")
    };

    static async Task Provision()
    {
        using var workspace = new DisposableWorkspace();
        var prod = Directory.CreateDirectory(Path.Combine(workspace.Root, "production")).FullName;
        var file = Path.Combine(prod, "bindings.json");
        await File.WriteAllTextAsync(file, """{"Index":"production-kb","Identity":"production-runtime"}""");
        var before = SHA256.HashData(await File.ReadAllBytesAsync(file));
        var desired = JsonSerializer.Deserialize<Bindings>(
            await File.ReadAllTextAsync("configs/test.json"))!;
        var first = await ApplyTest(workspace.Root, "test", desired);
        var second = await ApplyTest(workspace.Root, "test", desired);
        bool denied = false;
        try { await ApplyTest(workspace.Root, "production", desired); }
        catch (ArgumentException) { denied = true; }
        var after = SHA256.HashData(await File.ReadAllBytesAsync(file));
        var unchanged = before.SequenceEqual(after);
        Require(first && !second && denied && unchanged);
        Console.WriteLine($"Test index: {desired.Index}\nTest identity: {desired.Identity}");
        Console.WriteLine($"First apply wrote bindings: {first}\nSecond apply wrote bindings: {second}");
        Console.WriteLine($"Production target rejected: {denied}\nProduction bytes unchanged: {unchanged}");
    }

    static async Task<bool> ApplyTest(string root, string target, Bindings settings)
    {
        if (target != "test" || !settings.Index.StartsWith("test-", StringComparison.Ordinal) ||
            !settings.Identity.StartsWith("test-", StringComparison.Ordinal))
            throw new ArgumentException("Expected test-only target and bindings.");
        var folder = Directory.CreateDirectory(Path.Combine(root, "test")).FullName;
        var file = Path.Combine(folder, "bindings.json");
        var content = JsonSerializer.Serialize(settings);
        if (File.Exists(file) && await File.ReadAllTextAsync(file) == content) return false;
        await File.WriteAllTextAsync(file, content);
        return true;
    }

    static Task Identity()
    {
        var allowed = new HashSet<string> { "search.query", "search.write", "model.invoke" };
        Console.WriteLine($"Before removal | write allowed: {allowed.Contains("search.write")}");
        Require(allowed.Remove("search.write"));
        Require(allowed.SetEquals(["search.query", "model.invoke"]));
        Console.WriteLine($"After removal  | write allowed: {allowed.Contains("search.write")}");
        Console.WriteLine($"Search query allowed: {allowed.Contains("search.query")}");
        Console.WriteLine($"Model invoke allowed: {allowed.Contains("model.invoke")}");
        Console.WriteLine($"Blob read allowed: {allowed.Contains("blob.read")}");
        return Task.CompletedTask;
    }

    static Task Release()
    {
        using var workspace = new DisposableWorkspace();
        var current = Path.Combine(workspace.Root, "active.txt");
        var manifest = new ReleaseManifest("accepted", "prompt-v1", "stub-model-v1");
        var candidate = new ReleaseManifest("candidate", "prompt-v2", "stub-model-v1");
        File.WriteAllText(current, manifest.Revision);
        var accepted = File.ReadAllText(current);
        File.WriteAllText(current, candidate.Revision);
        Console.WriteLine($"Staging revision: {File.ReadAllText(current)} | {candidate.Prompt}");
        var expected = new[] { "KB-RESET", "KB-BILLING" };
        var observed = Evaluate(candidate);
        var passes = expected.Zip(observed).Count(pair => pair.First == pair.Second);
        var promoted = passes == expected.Length;
        if (!promoted) File.WriteAllText(current, accepted);
        Require(!promoted && Evaluate(manifest).SequenceEqual(expected) && File.ReadAllText(current) == accepted);
        Console.WriteLine($"Candidate citations: {string.Join(", ", observed)}");
        Console.WriteLine($"Gate: {passes}/{expected.Length} correct | required: {expected.Length}/{expected.Length}");
        Console.WriteLine($"Promoted: {promoted}\nRestored staging revision: {File.ReadAllText(current)}");
        return Task.CompletedTask;
    }

    static string[] Evaluate(ReleaseManifest manifest) =>
        manifest.Prompt == "prompt-v1" ? ["KB-RESET", "KB-BILLING"] : ["KB-RESET", "(missing)"];

    static async Task Observe(int modelDelay)
    {
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "Support.Operations",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
        };
        ActivitySource.AddActivityListener(listener);
        using var source = new ActivitySource("Support.Operations");
        using var request = source.StartActivity("POST /ask")!;
        Activity search;
        using (search = source.StartActivity("search.query")!)
            await Task.Delay(30);
        Activity model;
        using (model = source.StartActivity("model.invoke")!)
            await Task.Delay(modelDelay);
        request.Stop();
        bool alert = request.Duration >= TimeSpan.FromMilliseconds(200);
        Require(search.TraceId == request.TraceId && model.TraceId == request.TraceId);
        Console.WriteLine($"Trace: {request.TraceId}");
        Console.WriteLine($"POST /ask       {request.Duration.TotalMilliseconds:F0} ms");
        Console.WriteLine($"search.query    {search.Duration.TotalMilliseconds:F0} ms");
        Console.WriteLine($"model.invoke    {model.Duration.TotalMilliseconds:F0} ms");
        Console.WriteLine($"Local alert >= 200 ms: {alert}");
    }

    static async Task Load(int concurrency)
    {
        const int count = 12;
        using var slots = new SemaphoreSlim(concurrency);
        var measurements = new ConcurrentBag<int>();
        var timer = Stopwatch.StartNew();
        var active = 0;
        var tasks = Enumerable.Range(0, count).Select(async request =>
        {
            await slots.WaitAsync();
            measurements.Add(Interlocked.Increment(ref active));
            try
            {
                for (var attempt = 1; attempt <= 2; attempt++)
                {
                    await Task.Delay(30);
                    var available = request >= 4 && attempt == 2;
                    if (available) return (Answered: true, Attempts: attempt);
                    if (attempt == 1) await Task.Delay(20);
                }
                return (Answered: false, Attempts: 2);
            }
            finally
            {
                Interlocked.Decrement(ref active);
                slots.Release();
            }
        });
        var results = await Task.WhenAll(tasks);
        timer.Stop();
        var answered = results.Count(result => result.Answered);
        Require(answered == 8 && measurements.Max() == concurrency && results.Max(result => result.Attempts) == 2);
        Console.WriteLine($"Requests: {count} | max active: {measurements.Max()}");
        Console.WriteLine($"Answered: {answered} | fallback: {count - answered}");
        Console.WriteLine($"Maximum attempts per request: {results.Max(result => result.Attempts)}");
        Console.WriteLine($"Elapsed: {timer.Elapsed.TotalMilliseconds:F0} ms");
        Console.WriteLine($"Completed throughput: {count / timer.Elapsed.TotalSeconds:F1} req/s");
    }

    static async Task Optimize()
    {
        var cache = new Dictionary<CacheKey, string>();
        var calls = 0;
        async Task<string> Answer(string tenant, string user, string index, bool caching)
        {
            var key = new CacheKey(tenant, user, index, "reset", "prompt-v1", "stub-model-v1");
            if (caching && cache.TryGetValue(key, out var cached)) return cached;
            calls++;
            await Task.Delay(65);
            var answer = $"{tenant}/{user}/{index}/KB-RESET";
            if (caching) cache.Add(key, answer);
            return answer;
        }
        async Task<string[]> Workload(bool enabled) =>
        [
            await Answer("acme", "alice", "kb-v1", enabled),
            await Answer("acme", "alice", "kb-v1", enabled),
            await Answer("acme", "bob", "kb-v1", enabled),
            await Answer("acme", "alice", "kb-v2", enabled)
        ];
        var timer = Stopwatch.StartNew();
        var baseline = await Workload(false);
        Console.WriteLine($"Cache off | model stub calls: {calls} | {timer.ElapsedMilliseconds} ms");
        calls = 0;
        timer.Restart();
        var candidate = await Workload(true);
        Console.WriteLine($"Cache on  | model stub calls: {calls} | {timer.ElapsedMilliseconds} ms");
        var matches = baseline.SequenceEqual(candidate);
        var isolated = candidate[0] != candidate[2];
        var refreshed = candidate[0] != candidate[3];
        Require(calls == 3 && matches && isolated && refreshed);
        Console.WriteLine($"Same outputs: {matches}\nUser isolation: {isolated}\nIndex update caused miss: {refreshed}");
    }

    static Task Migrate()
    {
        var workspace = new DisposableWorkspace();
        var previous = Path.Combine(workspace.Root, "kb-v1.json");
        var candidate = Path.Combine(workspace.Root, "kb-v2.json");
        var pointer = Path.Combine(workspace.Root, "active.json");
        using (workspace)
        {
            File.WriteAllText(previous, """{"KB-RESET":"Use the account page."}""");
            File.WriteAllText(candidate, """{"KB-RESET":"Use Account Settings."}""");
            var release = new ReleaseManifest("kb-v1", "prompt-v1", "stub-model-v1");
            File.WriteAllText(pointer, JsonSerializer.Serialize(release));
            var next = release with { Revision = "kb-v2", Model = "stub-model-v2" };
            var corpus = JsonSerializer.Deserialize<Dictionary<string,string>>(File.ReadAllText(candidate))!;
            var healthy = corpus.TryGetValue("KB-RESET", out var document) && document == "Use Account Settings.";
            Require(healthy);
            File.WriteAllText(pointer + ".next", JsonSerializer.Serialize(next));
            File.Move(pointer + ".next", pointer, overwrite: true);
            var active = JsonSerializer.Deserialize<ReleaseManifest>(File.ReadAllText(pointer));
            Require(active == next && File.Exists(previous));
            Console.WriteLine($"Candidate citation smoke check: {healthy}");
            Console.WriteLine($"Active index: {active!.Revision} | {active.Model}");
            Console.WriteLine($"Previous index retained before teardown: {File.Exists(previous)}");
        }
        var removed = !Directory.Exists(workspace.Root);
        Require(removed);
        Console.WriteLine($"Disposable workspace removed: {removed}");
        return Task.CompletedTask;
    }

    static void Require(bool condition)
    {
        if (!condition) throw new InvalidOperationException("Checkpoint assertion failed.");
    }

    sealed record Bindings(string Index, string Identity);
    sealed record ReleaseManifest(string Revision, string Prompt, string Model);
    sealed record CacheKey(string Tenant, string User, string Index, string Question, string Prompt, string Model);

    sealed class DisposableWorkspace : IDisposable
    {
        public string Root { get; } = Directory.CreateTempSubdirectory("support-module-15-").FullName;
        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}
