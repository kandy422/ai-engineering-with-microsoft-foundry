using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Microsoft.ML.Tokenizers;

// The Module 09 encoder keeps vectorization consistent across the retrieval labs.
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
