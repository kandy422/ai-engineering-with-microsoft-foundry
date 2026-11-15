using System.Text.Json;

static partial class RetrievalLab
{
    static object Retrieve()
    {
        var query = JsonSerializer.Deserialize<RetrievalRequest>(Console.In.ReadToEnd())
            ?? throw new ArgumentException("A retrieval request is required.");
        if (string.IsNullOrWhiteSpace(query.Query) || query.Query.Length > 4000 ||
            query.Product is not ("laptop" or "tablet" or "account") ||
            query.TopK is < 1 or > 10)
            throw new ArgumentException("Provide a query, a supported product, and TopK between 1 and 10.");

        // The public checkpoint has a fixed trusted scope. Entitlements are not accepted from input.
        var documents = ReadDocuments().Where(doc => doc.Product == query.Product && doc.Scope == "public");
        using var encoder = new SentenceEncoder("lab/model");
        var indexed = Index(documents, encoder);
        return new { query.Query, query.Product, Scope = "public",
            Hits = VectorOrder(indexed, encoder.Embed(query.Query), query.TopK) };
    }

    private record RetrievalRequest(string Query, string Product, int TopK);
}
