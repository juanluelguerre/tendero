using System.Buffers.Binary;
using System.Security.Cryptography;

namespace ElGuerre.Tendero.AI.Speech;

/// <summary>
/// A spoken question, as the shop listens to it (the voice spec, V-1): a WAV of
/// 16 kHz, mono, 16-bit PCM — what the browser encodes from the microphone, and
/// what Whisper reads without converting. A short question, not a dictation.
///
/// **Held for the length of one transcription and by nothing else**: never written
/// to a store, a log or a trace (<c>docs/privacy.md</c>).
/// </summary>
public sealed class SpokenAudio
{
    public const int SampleRate = 16_000;

    /// <summary>The longest question listened to. Longer is a dictation, and refused.</summary>
    public const double MaxSeconds = 30;

    /// <summary>The shortest: a tap on the microphone that recorded nothing is not a question.</summary>
    public const double MinSeconds = 0.25;

    private const int HeaderBytes = 44;
    private const int BytesPerSecond = SampleRate * 2;

    /// <summary>The largest body that can hold <see cref="MaxSeconds"/> of audio and a header.</summary>
    public const int MaxBytes = HeaderBytes + (int)(MaxSeconds * BytesPerSecond) + 1024;

    private SpokenAudio(ReadOnlyMemory<byte> wav, TimeSpan length)
    {
        Wav = wav;
        Length = length;
    }

    /// <summary>The bytes as they arrived, header and all: what an engine is sent.</summary>
    public ReadOnlyMemory<byte> Wav { get; }

    public TimeSpan Length { get; }

    /// <summary>What a scripted engine knows this audio by: the hash of its bytes, never the bytes.</summary>
    public string Fingerprint => Convert.ToHexStringLower(SHA256.HashData(Wav.Span));

    /// <summary>The samples as floats in [-1, 1], for an engine in this process that reads them rather than the file.</summary>
    public float[] Samples()
    {
        TryFind(Wav.Span, "data"u8, out var data);
        var samples = new float[data.Length / 2];
        for (var i = 0; i < samples.Length; i++)
            samples[i] = BinaryPrimitives.ReadInt16LittleEndian(data[(i * 2)..]) / 32768f;
        return samples;
    }

    /// <summary>The audio, or why it is refused: a reason the slice says in each language.</summary>
    public static (SpokenAudio? Audio, AudioRefusal? Refusal) Read(ReadOnlyMemory<byte> bytes)
    {
        var span = bytes.Span;
        if (span.Length < HeaderBytes || !span[..4].SequenceEqual("RIFF"u8) || !span[8..12].SequenceEqual("WAVE"u8))
            return (null, AudioRefusal.NotWav);

        if (!TryFind(span, "fmt "u8, out var format) || format.Length < 16)
            return (null, AudioRefusal.NotWav);

        var encoding = BinaryPrimitives.ReadInt16LittleEndian(format);
        var channels = BinaryPrimitives.ReadInt16LittleEndian(format[2..]);
        var rate = BinaryPrimitives.ReadInt32LittleEndian(format[4..]);
        var bits = BinaryPrimitives.ReadInt16LittleEndian(format[14..]);
        if (encoding != 1 || channels != 1 || rate != SampleRate || bits != 16)
            return (null, AudioRefusal.WrongFormat);

        if (!TryFind(span, "data"u8, out var samples))
            return (null, AudioRefusal.NotWav);

        var length = TimeSpan.FromSeconds((double)samples.Length / BytesPerSecond);
        if (length > TimeSpan.FromSeconds(MaxSeconds))
            return (null, AudioRefusal.TooLong);
        if (length < TimeSpan.FromSeconds(MinSeconds))
            return (null, AudioRefusal.Empty);

        return (new SpokenAudio(bytes, length), null);
    }

    /// <summary>A chunk's body by its id, walking the chunks after the RIFF header.</summary>
    private static bool TryFind(ReadOnlySpan<byte> wav, ReadOnlySpan<byte> id, out ReadOnlySpan<byte> body)
    {
        for (var at = 12; at + 8 <= wav.Length;)
        {
            var size = BinaryPrimitives.ReadInt32LittleEndian(wav[(at + 4)..]);
            if (size < 0 || at + 8 + size > wav.Length)
                break;
            if (wav.Slice(at, 4).SequenceEqual(id))
            {
                body = wav.Slice(at + 8, size);
                return true;
            }

            at += 8 + size + (size & 1);
        }

        body = default;
        return false;
    }
}

/// <summary>Why a spoken question is not listened to. Each is a refusal with its code and its sentence in es and en, in the slice.</summary>
public enum AudioRefusal
{
    /// <summary>The bytes are not a WAV.</summary>
    NotWav,

    /// <summary>A WAV in another format than 16 kHz, mono, 16-bit PCM.</summary>
    WrongFormat,

    /// <summary>Longer than <see cref="SpokenAudio.MaxSeconds"/>: a dictation, not a question.</summary>
    TooLong,

    /// <summary>Shorter than <see cref="SpokenAudio.MinSeconds"/>: nothing was recorded.</summary>
    Empty,
}
