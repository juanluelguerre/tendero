using ElGuerre.Tendero.AI.Speech.Parakeet;
using Xunit;

namespace ElGuerre.Tendero.AI.Tests.Speech;

/// <summary>
/// Parakeet's features, written in C#, against the maker's own: the same audio
/// through transformers' <c>ParakeetFeatureExtractor</c> gave the numbers in the
/// fixture. A feature off by more than float noise is a transcript that drifts.
/// </summary>
public sealed class ParakeetFeaturesTests
{
    [Fact]
    public void The_mel_bank_is_librosas_slaney_bank() =>
        Assert.Equal(ParakeetFixture.MelSum, ParakeetFeatures.MelBankSum, 3);

    [Fact]
    public void An_utterance_has_the_makers_frames_and_the_makers_first_values()
    {
        var (values, _, valid) = ParakeetFeatures.Of(ParakeetFixture.Utterances[0].Samples);
        var expected = ParakeetFixture.Features;

        Assert.Equal(expected.GetProperty("frames").GetInt32(), valid);

        var worst = 0.0;
        var first = expected.GetProperty("first").EnumerateArray().ToArray();
        for (var frame = 0; frame < first.Length; frame++)
        {
            var mels = first[frame].EnumerateArray().Select(value => value.GetDouble()).ToArray();
            for (var mel = 0; mel < mels.Length; mel++)
                worst = Math.Max(worst, Math.Abs(values[frame * ParakeetFeatures.Mels + mel] - mels[mel]));
        }

        Assert.True(worst < 2e-3, $"The worst feature is {worst:G4} away from the maker's.");
    }

    /// <summary>
    /// The frame past the audio is padding: zero, as the attention mask says, and
    /// left out of each band's mean and deviation. Silence could not show it, since
    /// there every frame is zero anyway (phase 14's close).
    /// </summary>
    [Fact]
    public void The_frames_past_the_audio_are_zero()
    {
        var (values, frames, valid) = ParakeetFeatures.Of(ParakeetFixture.Utterances[0].Samples);

        Assert.True(frames > valid, $"{frames} frames, {valid} valid: no padding to look at.");
        for (var frame = valid; frame < frames; frame++)
            for (var mel = 0; mel < ParakeetFeatures.Mels; mel++)
                Assert.Equal(0f, values[frame * ParakeetFeatures.Mels + mel]);
        Assert.Contains(values.Take(valid * ParakeetFeatures.Mels), value => value != 0f);
    }

    [Fact]
    public void Silence_normalises_to_zeros_rather_than_to_noise()
    {
        var (values, frames, valid) = ParakeetFeatures.Of(new float[16_000]);

        Assert.Equal((101, 100), (frames, valid));
        Assert.All(values, value => Assert.Equal(0f, value));
    }

    /// <summary>Three stride-2 convolutions with a kernel of 3 and padding of 1: how many steps the encoder gives.</summary>
    [Theory]
    [InlineData(100, 13)]
    [InlineData(8, 1)]
    [InlineData(1, 1)]
    [InlineData(0, 0)]
    public void The_encoder_gives_one_step_for_every_eight_frames(int frames, int steps) =>
        Assert.Equal(steps, ParakeetOnnxSpeechToText.EncodedSteps(frames));
}
