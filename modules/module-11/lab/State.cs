record MemoryFact(string OrderId, DateTimeOffset ExpiresAt);
sealed class SessionStore(string path)
{
    public void Save(Caller caller, string session, MemoryFact fact)
    {
        var records = Read();
        records.RemoveAll(row => row.Caller == caller.Id && row.Session == session);
        records.Add(new(caller.Id, session, fact));
        Write(records);
    }

    public string? Resume(Caller caller, string session, DateTimeOffset now) =>
        Read().SingleOrDefault(row =>
            row.Caller == caller.Id && row.Session == session && now < row.Fact.ExpiresAt)?.Fact.OrderId;

    public void Forget(Caller caller, string session)
    {
        var records = Read();
        records.RemoveAll(row => row.Caller == caller.Id && row.Session == session);
        Write(records);
    }

    private List<MemoryRow> Read() => File.Exists(path)
        ? System.Text.Json.JsonSerializer.Deserialize<List<MemoryRow>>(File.ReadAllText(path))!
        : [];

    private void Write(List<MemoryRow> rows)
    {
        var pending = path + ".tmp";
        File.WriteAllText(pending, System.Text.Json.JsonSerializer.Serialize(rows));
        File.Move(pending, path, overwrite: true);
    }

    private sealed record MemoryRow(string Caller, string Session, MemoryFact Fact);
}

sealed class TicketStore
{
    private readonly Dictionary<(Caller, string), string> tickets = [];
    public int Count => tickets.Count;

    public string Create(Caller caller, string requestId)
    {
        lock (tickets)
        {
            var key = (caller, requestId);
            if (!tickets.TryGetValue(key, out var ticket))
            {
                ticket = $"T-{tickets.Count + 1}";
                tickets.Add(key, ticket);
            }
            return ticket;
        }
    }
}

record TicketChange(string Id, string Before, string After);
record AuditEntry(string ApprovalId, string Actor, TicketChange Change);
sealed class ApprovalGate
{
    private readonly Dictionary<string, (Caller Caller, TicketChange Change)> previews = [];
    private readonly Dictionary<string, AuditEntry> applied = [];
    private readonly HashSet<string> approved = [];
    private string currentStatus = "Open";
    public IReadOnlyCollection<AuditEntry> Audit => applied.Values;

    public void Preview(string id, Caller caller, TicketChange change)
    {
        lock (applied)
            previews.Add(id, (caller, change));
    }

    public bool Confirm(string id, Caller actor, TicketChange change)
    {
        lock (applied)
        {
            return previews.TryGetValue(id, out var preview) && preview == (actor, change) && approved.Add(id);
        }
    }

    public string Apply(string id, Caller caller, TicketChange change)
    {
        lock (applied)
        {
            if (!previews.TryGetValue(id, out var preview) || preview != (caller, change))
                return "denied";
            if (applied.ContainsKey(id))
                return "already_applied";
            if (!approved.Contains(id))
                return "approval_required";
            if (change.Id != "T-1" || change.Before != currentStatus)
                return "stale_preview";

            currentStatus = change.After;
            applied.Add(id, new(id, caller.Id, change));
            return "applied";
        }
    }
}


