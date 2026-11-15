using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

static partial class RetrievalLab
{
    const string ApiVersion = "2025-09-01";
    const string IndexName = "course09-support-v1";
    const string RecallQuery = "Can I return a laptop with a recall after the standard deadline?";

    static object AzurePayloads()
    {
        using var encoder = new SentenceEncoder("lab/model");
        var index = Index(ReadDocuments(), encoder);
        const int dimensions = 384;
        if (index.Any(item => item.Vector.Length != dimensions))
            throw new InvalidOperationException("The encoder and index dimensions differ.");
        var schema = JsonNode.Parse(File.ReadAllText("lab/azure-index.json"))!;
        if (schema["name"]!.GetValue<string>() != IndexName ||
            schema["fields"]!.AsArray().Single(field => field!["name"]!.GetValue<string>() == "contentVector")!["dimensions"]!.GetValue<int>() != dimensions)
            throw new InvalidOperationException("Index definition does not match the encoder contract.");

        var docs = index.Select(item => new Dictionary<string, object> {
            ["@search.action"] = "mergeOrUpload", ["id"] = item.Doc.Id,
            ["product"] = item.Doc.Product, ["scope"] = item.Doc.Scope,
            ["topic"] = item.Doc.Topic, ["content"] = item.Doc.Content, ["contentVector"] = item.Vector
        }).ToArray();
        var vector = encoder.Embed(RecallQuery);
        return new {
            ApiVersion, IndexName, Schema = schema, Upload = new { value = docs },
            Vector = QueryPayload(vector, null, semantic: false),
            Hybrid = QueryPayload(vector, RecallQuery, semantic: false),
            Semantic = QueryPayload(vector, RecallQuery, semantic: true),
            Verification = new { Dimensions = dimensions, Documents = docs.Length,
                UniqueKeys = docs.Select(doc => doc["id"]).Distinct().Count(),
                QueryCoordinates = vector.Take(4).ToArray(),
                Execution = "Generated locally; no HTTP requests sent." }
        };
    }

    static object QueryPayload(float[] vector, string? text, bool semantic)
    {
        var request = new Dictionary<string, object> {
            ["top"] = 3,
            ["select"] = "id,product,content",
            ["filter"] = "product eq 'laptop' and scope eq 'public'",
            ["vectorFilterMode"] = "preFilter",
            ["vectorQueries"] = new[] { new { kind = "vector", vector, fields = "contentVector", k = semantic ? 50 : 3 } }
        };
        if (text is not null) request["search"] = text;
        if (semantic) {
            request["queryType"] = "semantic";
            request["semanticConfiguration"] = "policy-content";
        }
        return request;
    }
}
