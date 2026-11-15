static class DefensiveChecks
{
    public static int Run(string lesson)
    {
        switch (lesson)
        {
            case "14.2":
                Group("gate");
                Check("Authorised lookup", Capability("LookupOrder"), "Allowed");
                Check("Unsupported capability fixture", Capability("Unavailable"), "Denied");
                Check("Suspect document fixture", ReviewDocument(true), "Review");
                break;
            case "14.3":
                Identity();
                break;
            case "14.4":
                Privacy();
                break;
            case "14.5":
                Group("routes");
                Check("Allow", Route("Allow"), "Continue");
                Check("Block", Route("Block"), "Support");
                Check("Unavailable", Route("Unavailable"), "Human review");
                break;
            case "14.6":
                Group("pair");
                Check("What is my delivery status?", Classify("What is my delivery status?"), "Delivery status");
                Check("Any news on my parcel?", Classify("Any news on my parcel?"), "Review");
                break;
            case "14.7":
                foreach (var prior in new[] { "14.2", "14.3", "14.4", "14.5", "14.6" }) Run(prior);
                Group("approval");
                Check("Action with matching approval", Approval("request-104","request-104"), "Allowed");
                Check("Action with different approval", Approval("request-105","request-104"), "Denied");
                Group("release");
                Check("Required live deployment evidence", DeploymentEvidence(false), "Pending");
                Check("Release decision", Release(localChecksPassed: true, liveVerified: false), "Hold");
                break;
            default:
                throw new ArgumentException($"Unknown lesson: {lesson}");
        }
        return 0;
    }

    static string Capability(string proposed) => proposed == "LookupOrder" ? "Allowed" : "Denied";
    static string ReviewDocument(bool suspect) => suspect ? "Review" : "Continue";

    static void Identity()
    {
        var principal = new Principal("north","customer-a");
        Group("documents");
        Check("North delivery policy", Document(principal,"north","customer"), "Included");
        Check("North staff policy", Document(principal,"north","staff"), "Excluded");
        Group("orders");
        Check("A-104 / same owner", Lookup(principal,new Order("A-104","north","customer-a")), "Allowed");
        Check("A-105 / different owner", Lookup(principal,new Order("A-105","north","customer-b")), "Denied");
        Check("B-104 / different tenant", Lookup(principal,new Order("B-104","south","customer-a")), "Denied");
    }

    static string Document(Principal principal, string tenant, string audience) =>
        principal.Tenant == tenant && audience == "customer" ? "Included" : "Excluded";
    static string Lookup(Principal principal, Order order) =>
        principal.Tenant == order.Tenant && principal.Customer == order.Customer ? "Allowed" : "Denied";

    static void Privacy()
    {
        var record = new CustomerRecord("c-01","customer@example.test","synthetic private note");
        var store = new Dictionary<string,CustomerRecord> { [record.Id] = record };
        var cache = new Dictionary<string,CustomerRecord>(store);
        var entry = System.Text.Json.JsonSerializer.Serialize(new { outcome = "resolved" });
        Group("privacy");
        Check("Telemetry", entry, "{\"outcome\":\"resolved\"}");
        Check("Private values in telemetry", entry.Contains(record.Email) || entry.Contains(record.Note) ? "Present" : "Absent", "Absent");
        Group("deletion");
        Delete(record.Id,store,cache);
        Check("Remaining store + cache entries", (store.Count+cache.Count).ToString(), "0");
        Delete(record.Id,store,cache);
        Check("Entries after repeat", (store.Count+cache.Count).ToString(), "0");
    }

    static void Delete(string id, Dictionary<string,CustomerRecord> store, Dictionary<string,CustomerRecord> cache)
    {
        store.Remove(id);
        cache.Remove(id);
    }

    static string Route(string signal) => signal switch
    {
        "Allow" => "Continue",
        "Block" => "Support",
        _ => "Human review"
    };

    static string Classify(string request) =>
        request.Contains("delivery status", StringComparison.OrdinalIgnoreCase) ? "Delivery status" : "Review";

    static string Approval(string requested, string approved) => requested == approved ? "Allowed" : "Denied";
    static string DeploymentEvidence(bool liveVerified) => liveVerified ? "Verified" : "Pending";
    static string Release(bool localChecksPassed, bool liveVerified) => localChecksPassed && liveVerified ? "Proceed" : "Hold";

    static void Group(string name) => Console.WriteLine($"\n{name}");

    static void Check(string input, string actual, string expected)
    {
        Console.WriteLine($"{input} => {actual}");
        if (actual != expected)
            throw new InvalidOperationException($"Expected {expected}, observed {actual}");
    }

    record CustomerRecord(string Id,string Email,string Note);
}
