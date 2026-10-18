using System.Buffers.Binary;

namespace ElGuerre.Tendero.AI.Tests.Speech;

/// <summary>WAV files written by hand for the tests: a header and PCM samples, nothing recorded.</summary>
internal static class Wav
{
    public static byte[] Silence(double seconds, int sampleRate = 16_000, short channels = 1, short bitsPerSample = 16) =>
        Of(new byte[(int)(seconds * sampleRate) * channels * (bitsPerSample / 8)], sampleRate, channels, bitsPerSample);

    /// <summary>A 440 Hz tone: sound, and no words in it.</summary>
    public static byte[] Tone(double seconds)
    {
        const int rate = 16_000;
        var samples = new byte[(int)(seconds * rate) * 2];
        for (var i = 0; i < samples.Length / 2; i++)
            BinaryPrimitives.WriteInt16LittleEndian(samples.AsSpan(i * 2), (short)(8_000 * Math.Sin(2 * Math.PI * 440 * i / rate)));
        return Of(samples, rate, 1, 16);
    }

    public static byte[] Of(byte[] samples, int sampleRate, short channels, short bitsPerSample)
    {
        var wav = new byte[44 + samples.Length];
        var span = wav.AsSpan();
        "RIFF"u8.CopyTo(span);
        BinaryPrimitives.WriteInt32LittleEndian(span[4..], 36 + samples.Length);
        "WAVE"u8.CopyTo(span[8..]);
        "fmt "u8.CopyTo(span[12..]);
        BinaryPrimitives.WriteInt32LittleEndian(span[16..], 16);
        BinaryPrimitives.WriteInt16LittleEndian(span[20..], 1);
        BinaryPrimitives.WriteInt16LittleEndian(span[22..], channels);
        BinaryPrimitives.WriteInt32LittleEndian(span[24..], sampleRate);
        BinaryPrimitives.WriteInt32LittleEndian(span[28..], sampleRate * channels * bitsPerSample / 8);
        BinaryPrimitives.WriteInt16LittleEndian(span[32..], (short)(channels * bitsPerSample / 8));
        BinaryPrimitives.WriteInt16LittleEndian(span[34..], bitsPerSample);
        "data"u8.CopyTo(span[36..]);
        BinaryPrimitives.WriteInt32LittleEndian(span[40..], samples.Length);
        samples.CopyTo(span[44..]);
        return wav;
    }
}
