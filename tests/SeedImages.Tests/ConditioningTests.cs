using ElGuerre.Tendero.SeedImages;
using Xunit;

namespace ElGuerre.Tendero.SeedImages.Tests;

/// <summary>
/// What the UNet is told, besides the noise.
///
/// SDXL takes three conditioning inputs and the arrangement is the part nobody
/// guesses right: the hidden states of **two** text towers concatenated along
/// their channels, a pooled embedding that comes from the **second tower only**,
/// and a six-number vector describing the size the picture is pretending to be.
///
/// All of it is plumbing, and plumbing is testable: the towers here are
/// deterministic fakes that return recognisable numbers, so a channel landing in
/// the wrong half of the tensor is an assertion rather than a bad image.
/// </summary>
public sealed class ConditioningTests
{
    /// <summary>A tower that returns a constant per channel, so every value can
    /// be traced back to which tower produced it.</summary>
    private sealed class FlatTower(int width, float hidden, float pooled) : ITextTower
    {
        public int Width => width;

        public int Calls { get; private set; }

        public TowerOutput Encode(int[] tokenIds)
        {
            Calls++;

            var states = new float[tokenIds.Length * width];
            Array.Fill(states, hidden + Calls);

            var pooledValues = new float[1280];
            Array.Fill(pooledValues, pooled + Calls);

            return new TowerOutput(states, pooledValues);
        }

        public void Dispose() { }
    }

    private static TextConditioning Build(FlatTower first, FlatTower second) =>
        TextConditioning.Build(
            first, new TextConditioning.PromptTokens([1, 2, 3], [4, 5, 6]),
            second, new TextConditioning.PromptTokens([1, 2, 3], [4, 5, 6]));

    [Fact]
    public void The_two_towers_are_concatenated_into_the_width_the_unet_expects()
    {
        var conditioning = Build(new FlatTower(768, 10f, 100f), new FlatTower(1280, 20f, 200f));

        // Batch of two -- the negative and the positive -- times the token count,
        // times 768 + 1280.
        Assert.Equal(2 * 3 * 2048, conditioning.HiddenStates.Length);
        Assert.Equal(2 * 1280, conditioning.PooledEmbeds.Length);
    }

    /// <summary>
    /// **The first tower owns the low channels and the second owns the high ones.**
    /// Swapping them produces a tensor of exactly the right shape that means
    /// something else entirely, and the only symptom is that every image ignores
    /// the prompt a little.
    /// </summary>
    [Fact]
    public void The_first_tower_fills_the_low_channels_and_the_second_the_high_ones()
    {
        var conditioning = Build(new FlatTower(768, 10f, 100f), new FlatTower(1280, 20f, 200f));

        Assert.Equal(11f, conditioning.HiddenStates[0]);
        Assert.Equal(11f, conditioning.HiddenStates[767]);
        Assert.Equal(21f, conditioning.HiddenStates[768]);
        Assert.Equal(21f, conditioning.HiddenStates[2047]);
    }

    /// <summary>
    /// The pooled embedding is the **second** tower's projected output and the
    /// first tower's is thrown away. It is 1280 wide either way, so using the
    /// wrong one costs nothing that a shape check would notice.
    /// </summary>
    [Fact]
    public void The_pooled_embedding_comes_from_the_second_tower_and_not_the_first()
    {
        var conditioning = Build(new FlatTower(768, 10f, 100f), new FlatTower(1280, 20f, 200f));

        Assert.Equal(201f, conditioning.PooledEmbeds[0]);
        Assert.DoesNotContain(101f, conditioning.PooledEmbeds);
    }

    /// <summary>
    /// **The negative prompt is row zero.** Guidance subtracts one row from the
    /// other, so getting the order backwards does not fail — it steers the image
    /// towards everything the negative prompt was meant to keep out.
    /// </summary>
    [Fact]
    public void The_negative_prompt_is_the_first_row_of_the_batch()
    {
        var first = new FlatTower(768, 10f, 100f);
        var conditioning = TextConditioning.Build(
            first, new TextConditioning.PromptTokens([1, 2, 3], [4, 5, 6]),
            new FlatTower(1280, 20f, 200f), new TextConditioning.PromptTokens([1, 2, 3], [4, 5, 6]));

        // The fake counts its calls, so the first row carries a smaller number
        // than the second exactly when the negative was encoded first.
        var rowLength = 3 * 2048;

        Assert.True(
            conditioning.HiddenStates[0] < conditioning.HiddenStates[rowLength],
            "The negative prompt must be encoded into row 0 and the positive into row 1.");
    }

    /// <summary>
    /// The micro-conditioning SDXL adds: the size the source is claimed to be, the
    /// crop it is claimed to come from, and the size being asked for. All 1024,
    /// **not 1400** -- the model was trained conditioned on the resolution it
    /// actually produces, and the 1400 is a resize that happens afterwards.
    /// </summary>
    [Fact]
    public void The_time_ids_describe_the_size_the_model_makes_and_not_the_file_we_want()
    {
        var ids = TimeIds.For(1024, batch: 2);

        Assert.Equal(12, ids.Length);
        Assert.Equal([1024f, 1024f, 0f, 0f, 1024f, 1024f], ids[..6]);
        Assert.Equal(ids[..6], ids[6..]);
    }
}
