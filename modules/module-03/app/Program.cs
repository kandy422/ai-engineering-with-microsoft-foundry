using System.Diagnostics;
using System.Text.Json;

var request = await Console.In.ReadToEndAsync();
string? ticket = null;
try
{
    using var doc = JsonDocument.Parse(request);
    if (doc.RootElement.ValueKind == JsonValueKind.Object &&
        doc.RootElement.TryGetProperty("text", out var field) &&
        field.ValueKind == JsonValueKind.String)
        ticket = field.GetString();
}
catch (JsonException) { }
if (string.IsNullOrWhiteSpace(ticket) || ticket.Length > 500)
{
    Console.WriteLine(JsonSerializer.Serialize(new { status = "invalid", queue = (string?)null }));
    return 0;
}

var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../"));
var py = new ProcessStartInfo
{
    FileName = Environment.GetEnvironmentVariable("COURSE_PYTHON") ?? "python",
    RedirectStandardInput = true,
    RedirectStandardOutput = true,
    RedirectStandardError = true,
    UseShellExecute = false
};
py.ArgumentList.Add(Path.Combine(root, "lab", "experiments.py"));
py.ArgumentList.Add("route");
Process? process;
try { process = Process.Start(py); }
catch (System.ComponentModel.Win32Exception)
{
    Console.WriteLine("{\"status\":\"unavailable\",\"queue\":null}");
    return 1;
}
if (process is null)
{
    Console.WriteLine("{\"status\":\"unavailable\",\"queue\":null}");
    return 1;
}
using var child = process;
await child.StandardInput.WriteAsync(JsonSerializer.Serialize(ticket));
child.StandardInput.Close();
var stdout = child.StandardOutput.ReadToEndAsync();
var stderr = child.StandardError.ReadToEndAsync();
using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
try { await child.WaitForExitAsync(deadline.Token); }
catch (OperationCanceledException)
{
    child.Kill(entireProcessTree: true);
    await child.WaitForExitAsync();
    Console.WriteLine("{\"status\":\"unavailable\",\"queue\":null}");
    return 1;
}
if (child.ExitCode != 0)
{
    Console.Error.WriteLine(await stderr);
    Console.WriteLine("{\"status\":\"unavailable\",\"queue\":null}");
    return 1;
}
Console.WriteLine(await stdout);
return 0;
