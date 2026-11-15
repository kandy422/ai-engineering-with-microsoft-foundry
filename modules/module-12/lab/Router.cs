internal record Intake(string Name, string Mime, string Task, int Bytes);
internal record Decision(string State, string Target);

internal static class Router
{
    internal const int MaxBytes = 2_000_000;

    internal static Decision Route(Intake input) =>
        (input.Mime, input.Task) switch
        {
            _ when input.Bytes <= 0 => new("rejected", "empty input"),
            _ when input.Bytes > MaxBytes => new("rejected", "app byte limit"),
            ("text/plain", "summarize") => new("routed", "text model"),
            ("image/jpeg", "inspect") => new("routed", "vision model"),
            ("image/jpeg", "extract") => new("routed", "receipt extraction"),
            ("audio/wav", "transcribe") => new("routed", "speech recognition"),
            _ => new("rejected", "unsupported task or format")
        };
}
