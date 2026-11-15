using System.Text.Json;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Microsoft.ML.Tokenizers;

if (args.Length > 0)
{
    try
    {
        if (args.Length != 1) throw new ArgumentException("Supply one lab command.");
        RetrievalLab.Run(args[0]);
    }
    catch (Exception error) when (error is ArgumentException or JsonException)
    {
        Console.Error.WriteLine(error.Message);
        Environment.ExitCode = 2;
    }
    return;
}

var data = JsonSerializer.Deserialize<Questions>(File.ReadAllText("lab/questions.json"))!;
using var encoder = new SentenceEncoder("lab/model");
var queryVector = encoder.Embed(data.Query);
var candidates = data.Candidates
    .Select(candidate => new { candidate.Id, candidate.Text, Vector = encoder.Embed(candidate.Text) })
    .ToArray();
var neighbors = candidates
    .Select(item => new { item.Id, item.Text, Score = Similarity.Cosine(queryVector, item.Vector) })
    .OrderByDescending(item => item.Score)
    .ThenBy(item => item.Id, StringComparer.Ordinal)
    .ToArray();

Console.WriteLine(JsonSerializer.Serialize(new
{
    Model = "sentence-transformers/all-MiniLM-L6-v2",
    Revision = File.ReadAllText("lab/model/revision.txt").Trim(),
    data.Query,
    Dimensions = queryVector.Length,
    QueryVector = queryVector,
    Candidates = candidates,
    Neighbors = neighbors
}, new JsonSerializerOptions { WriteIndented = true }));

record Questions(string Query, Candidate[] Candidates);
record Candidate(string Id, string Text);

sealed class SentenceEncoder : IDisposable
{
    private readonly BertTokenizer tokenizer;
    private readonly InferenceSession session;

    public SentenceEncoder(string directory)
    {
        tokenizer = BertTokenizer.Create(Path.Combine(directory, "vocab.txt"));
        using var options = new SessionOptions { IntraOpNumThreads = 2, InterOpNumThreads = 1 };
        session = new InferenceSession(Path.Combine(directory, "model.onnx"), options);
    }

    public float[] Embed(string text)
    {
        var ids = tokenizer.EncodeToIds(text, 256, true, out _, out _)
            .Select(id => (long)id).ToArray();
        int[] shape = [1, ids.Length];
        var inputs = new[]
        {
            NamedOnnxValue.CreateFromTensor("input_ids", new DenseTensor<long>(ids, shape)),
            NamedOnnxValue.CreateFromTensor("attention_mask",
                new DenseTensor<long>(Enumerable.Repeat(1L, ids.Length).ToArray(), shape)),
            NamedOnnxValue.CreateFromTensor("token_type_ids", new DenseTensor<long>(new long[ids.Length], shape))
        };
        using var outputs = session.Run(inputs);
        var hidden = outputs.Single(output => output.Name == "last_hidden_state").AsTensor<float>();
        var pooled = new float[hidden.Dimensions[2]];
        for (var token = 0; token < ids.Length; token++)
            for (var axis = 0; axis < pooled.Length; axis++)
                pooled[axis] += hidden[0, token, axis] / ids.Length;

        var length = Math.Sqrt(pooled.Sum(value => (double)value * value));
        return pooled.Select(value => (float)(value / length)).ToArray();
    }

    public void Dispose() => session.Dispose();
}

static class Similarity
{
    public static double Cosine(float[] left, float[] right)
    {
        if (left.Length != right.Length) throw new ArgumentException("Vector dimensions differ.");
        var dot = left.Zip(right).Sum(pair => (double)pair.First * pair.Second);
        var leftLength = Math.Sqrt(left.Sum(value => (double)value * value));
        var rightLength = Math.Sqrt(right.Sum(value => (double)value * value));
        if (leftLength == 0 || rightLength == 0) throw new ArgumentException("Cosine requires nonzero vectors.");
        return dot / (leftLength * rightLength);
    }
}


