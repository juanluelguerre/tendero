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
}
