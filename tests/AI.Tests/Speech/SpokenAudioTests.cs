using ElGuerre.Tendero.AI.Speech;
using Xunit;

namespace ElGuerre.Tendero.AI.Tests.Speech;

/// <summary>
/// What the shop listens to (the voice spec, V-1): a short question, as the
/// browser encodes it — 16 kHz, mono, 16-bit PCM in a WAV. Anything else is
/// refused by its bytes, with its reason, before an engine is asked anything.
/// </summary>
public sealed class SpokenAudioTests
{
    [Fact]
    public void A_short_question_in_the_shops_format_is_read_with_its_length()
    {
        var read = SpokenAudio.Read(Wav.Silence(2.5));

        Assert.Null(read.Refusal);
        Assert.Equal(TimeSpan.FromSeconds(2.5), read.Audio!.Length);
    }

    [Theory]
    [InlineData("not a wav at all")]
    [InlineData("RIFF")]
    public void Bytes_that_are_not_a_wav_are_refused(string text) =>
        Assert.Equal(AudioRefusal.NotWav, SpokenAudio.Read(System.Text.Encoding.ASCII.GetBytes(text)).Refusal);

    public static TheoryData<byte[]> OtherFormats => new()
    {
        Wav.Silence(1, sampleRate: 44_100),
        Wav.Silence(1, channels: 2),
        Wav.Silence(1, bitsPerSample: 8),
    };

    [Theory]
    [MemberData(nameof(OtherFormats))]
    public void A_wav_in_another_format_is_refused(byte[] wav) =>
        Assert.Equal(AudioRefusal.WrongFormat, SpokenAudio.Read(wav).Refusal);

    /// <summary>A question, not a dictation: longer is refused, and so is a tap that recorded nothing.</summary>
    [Theory]
    [InlineData(SpokenAudio.MaxSeconds + 0.5, AudioRefusal.TooLong)]
    [InlineData(0.1, AudioRefusal.Empty)]
    public void Too_long_or_too_short_is_refused(double seconds, AudioRefusal reason) =>
        Assert.Equal(reason, SpokenAudio.Read(Wav.Silence(seconds)).Refusal);

    [Fact]
    public void The_longest_question_is_still_read() =>
        Assert.Null(SpokenAudio.Read(Wav.Silence(SpokenAudio.MaxSeconds)).Refusal);

    [Fact]
    public void The_shortest_question_is_still_read() =>
        Assert.Null(SpokenAudio.Read(Wav.Silence(SpokenAudio.MinSeconds)).Refusal);

    /// <summary>
    /// The chunks a WAV can carry besides its format and its samples, walked as the
    /// format says (phase 14's close found every case below unheld): a header with no
    /// samples is a tap that recorded nothing, a format chunk too short to hold a
    /// format is no WAV, a chunk of odd size is followed by one byte of padding, and a
    /// chunk that claims more than the file holds ends the walk.
    /// </summary>
    [Fact]
    public void A_header_with_no_samples_is_empty() =>
        Assert.Equal(AudioRefusal.Empty, SpokenAudio.Read(Wav.Silence(0)).Refusal);

    [Fact]
    public void A_format_chunk_too_short_for_a_format_is_no_wav()
    {
        var format = Wav.Silence(1).AsSpan(20, 16).ToArray()[..14];

        Assert.Equal(AudioRefusal.NotWav, SpokenAudio.Read(Chunks(("fmt ", format), ("data", new byte[16_000]))).Refusal);
    }

    [Fact]
    public void A_chunk_of_odd_size_is_followed_by_its_padding_byte()
    {
        var wav = Wav.Silence(1);
        var read = SpokenAudio.Read(Chunks(("LIST", new byte[3]), ("fmt ", wav.AsSpan(20, 16).ToArray()), ("data", wav.AsSpan(44).ToArray())));

        Assert.Null(read.Refusal);
        Assert.Equal(TimeSpan.FromSeconds(1), read.Audio!.Length);
    }

    [Fact]
    public void A_chunk_that_claims_more_than_the_file_ends_the_walk()
    {
        var wav = Wav.Silence(1);
        var bytes = Chunks(("fmt ", wav.AsSpan(20, 16).ToArray()), ("LIST", new byte[4]), ("data", wav.AsSpan(44).ToArray()));
        // The LIST chunk now says it holds a megabyte: the data after it is never reached.
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(12 + 8 + 16 + 4), 1 << 20);

        Assert.Equal(AudioRefusal.NotWav, SpokenAudio.Read(bytes).Refusal);
    }

    /// <summary>A RIFF/WAVE file of these chunks, each padded to an even length as the format says.</summary>
    private static byte[] Chunks(params (string Id, byte[] Body)[] chunks)
    {
        var body = new List<byte>("WAVE"u8.ToArray());
        foreach (var (id, content) in chunks)
        {
            body.AddRange(System.Text.Encoding.ASCII.GetBytes(id));
            body.AddRange(BitConverter.GetBytes(content.Length));
            body.AddRange(content);
            if (content.Length % 2 == 1)
                body.Add(0);
        }

        return [.. "RIFF"u8.ToArray(), .. BitConverter.GetBytes(body.Count), .. body];
    }
}
