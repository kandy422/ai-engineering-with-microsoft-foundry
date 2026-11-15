using System.Buffers.Binary;

internal static class VoiceLoop
{
    // Real PCM blocks are sent to a deterministic stream for this local checkpoint.
    // A production sink owns its hardware queue and must flush on cancellation.
    internal static int Play(Stream sink, byte[] pcm, CancellationToken token, Action<int> onBlock)
    {
        const int blockBytes = 960; // 20 ms at mono, 24 kHz, 16-bit PCM
        var blocks = 0;
        for (var offset = 0; offset < pcm.Length && !token.IsCancellationRequested; offset += blockBytes)
        {
            sink.Write(pcm, offset, Math.Min(blockBytes, pcm.Length - offset));
            onBlock(++blocks);
        }
        return blocks;
    }

    internal static byte[] ReadPcm(string path)
    {
        var wav = File.ReadAllBytes(path);
        if (wav.Length < 12 || !wav.AsSpan(0, 4).SequenceEqual("RIFF"u8) ||
            !wav.AsSpan(8, 4).SequenceEqual("WAVE"u8))
            throw new InvalidDataException("Expected RIFF WAVE.");
        var supported = false;
        for (var cursor = 12; cursor + 8 <= wav.Length;)
        {
            var tag = wav.AsSpan(cursor, 4);
            var count = BinaryPrimitives.ReadInt32LittleEndian(wav.AsSpan(cursor+4,4));
            var start = cursor + 8;
            if (count < 0 || count > wav.Length-start) throw new InvalidDataException("Truncated WAV chunk.");
            var payload = wav.AsSpan(start,count);
            if (tag.SequenceEqual("fmt "u8) && count >= 16)
                supported = BinaryPrimitives.ReadInt16LittleEndian(payload) == 1
                    && BinaryPrimitives.ReadInt16LittleEndian(payload[2..]) == 1
                    && BinaryPrimitives.ReadInt32LittleEndian(payload[4..]) == 24000
                    && BinaryPrimitives.ReadInt16LittleEndian(payload[14..]) == 16;
            if (tag.SequenceEqual("data"u8) && supported) return payload.ToArray();
            cursor = start + count + count%2;
        }
        throw new InvalidDataException("Expected mono 24 kHz PCM16.");
    }
}
