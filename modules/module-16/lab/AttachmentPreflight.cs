record AttachmentMetadata(string Mime, string Task, int Bytes);
record AttachmentDecision(string State, string Target);

static class AttachmentPreflight
{
    // Metadata contract adapted from Module 12's Router checkpoint.
    private const int MaxBytes = 2_000_000;

    public static AttachmentDecision Check(AttachmentMetadata input) =>
        (input.Mime, input.Task) switch
        {
            _ when input.Bytes <= 0 => new("rejected", "empty input"),
            _ when input.Bytes > MaxBytes => new("rejected", "app byte limit"),
            ("image/jpeg", "extract") => new("routed", "receipt extraction"),
            _ => new("rejected", "unsupported task or format")
        };
}
