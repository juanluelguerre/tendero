using ElGuerre.Tendero.SeedImages;
using Xunit;

namespace ElGuerre.Tendero.SeedImages.Tests;

/// <summary>
/// The walk, and the noise it starts from. Both pure, both tested with no GPU and
/// no weights — which is the whole reason the pipeline is cut here.
/// </summary>
public sealed class DiffusionLoopTests
{
    private static EulerDiscreteScheduler Scheduler(int steps) =>
        EulerDiscreteScheduler.Create(steps, 0.00085f, 0.012f, 1000, 1);

    private static TextConditioning NoText(int length) =>
        new(new float[2 * length], new float[2 * 1280], 1);

    /// <summary>A model that hands back exactly the noise it was told about, in
    /// both rows. It is what makes the walk telescope.</summary>
    private sealed class EchoUnet(float[] noise) : IUnet
    {
        public int Calls { get; private set; }

        public float[]? LastSample { get; private set; }

        public float[] Predict(float[] sample, float timestep, TextConditioning text, float[] timeIds)
        {
            Calls++;
            LastSample = sample;

            var prediction = new float[sample.Length];
            noise.CopyTo(prediction, 0);
            noise.CopyTo(prediction, noise.Length);

            return prediction;
        }

        public void Dispose() { }
    }

    /// <summary>A model whose two rows disagree, so guidance has something to
    /// amplify.</summary>
    private sealed class SplitUnet(float unconditional, float conditional) : IUnet
    {
        public float[] Predict(float[] sample, float timestep, TextConditioning text, float[] timeIds)
        {
            var half = sample.Length / 2;
            var prediction = new float[sample.Length];

            Array.Fill(prediction, unconditional, 0, half);
            Array.Fill(prediction, conditional, half, half);

            return prediction;
        }

        public void Dispose() { }
    }

    /// <summary>
    /// **The test that proves the loop, and it never touches a model.**
    ///
    /// The Euler update is `latents += prediction * (sigma[next] - sigma[now])`,
    /// so a model that always returns the noise the latent was built from makes
    /// the sum telescope: every step cancels against the next, the last sigma is
    /// zero, and what is left is the latent scaled back to nothing.
    ///
    /// Starting from `noise * initialSigma` and removing exactly that noise at
    /// every rung, the walk has to end at zero. One assertion covering the sigma
    /// ladder, the initial scaling, the step direction, the accumulation and the
    /// off-by-one in `sigma[i + 1]`.
    /// </summary>
    [Fact]
    public void A_model_that_returns_the_noise_it_was_given_denoises_all_the_way_to_nothing()
    {
        var scheduler = Scheduler(12);
        float[] noise = [1f, -0.5f, 2f, 0.75f];

        var final = UnetLoop.Run(scheduler, new EchoUnet(noise), noise, NoText(4), TimeIds.For(1024, 2), guidance: 1f);

        // The walk starts at noise * initialSigma and the ladder's last rung is
        // zero, so removing exactly that noise every time lands on the ground.
        // The gap between InitialNoiseSigma and sigma[0] is the scaling the
        // schedule applies on the way in, and it is what remains.
        var remainder = scheduler.InitialNoiseSigma - scheduler.Sigmas[0];

        for (var index = 0; index < noise.Length; index++)
            Assert.Equal(noise[index] * remainder, final[index], 3);
    }

    [Fact]
    public void The_model_is_asked_once_per_step()
    {
        var scheduler = Scheduler(7);
        float[] noise = [1f, 1f];
        var unet = new EchoUnet(noise);

        UnetLoop.Run(scheduler, unet, noise, NoText(2), TimeIds.For(1024, 2), guidance: 7f);

        Assert.Equal(7, unet.Calls);
    }

    /// <summary>
    /// The model sees the same latent twice, once per conditioning row. A batch
    /// that carried two different latents would be a bug the shapes would not
    /// catch.
    /// </summary>
    [Fact]
    public void Both_rows_of_the_batch_carry_the_same_latent()
    {
        var scheduler = Scheduler(3);
        float[] noise = [1f, -1f, 0.5f];
        var unet = new EchoUnet(noise);

        UnetLoop.Run(scheduler, unet, noise, NoText(3), TimeIds.For(1024, 2), guidance: 7f);

        var sample = unet.LastSample!;

        Assert.Equal(6, sample.Length);
        Assert.Equal(sample[..3], sample[3..]);
    }

    /// <summary>
    /// **Guidance at 1.0 is no guidance.** The formula is
    /// `uncond + g * (cond - uncond)`, so at one it collapses to the conditional
    /// answer — which is the arithmetic check that the two rows are not swapped.
    /// At 7 the answer is pushed well past the conditional one, away from the
    /// negative prompt, and that overshoot is the entire reason a negative prompt
    /// does anything.
    /// </summary>
    [Theory]
    [InlineData(1f, 3f)]
    [InlineData(0f, 1f)]
    [InlineData(7f, 15f)]
    public void Guidance_amplifies_the_distance_from_the_unconditional_answer(float guidance, float expected)
    {
        var scheduler = Scheduler(1);
        float[] noise = [1f];

        var final = UnetLoop.Run(
            scheduler, new SplitUnet(unconditional: 1f, conditional: 3f),
            noise, NoText(1), TimeIds.For(1024, 2), guidance);

        // One step, so the walk is: start + guided * (0 - sigma[0]).
        var start = noise[0] * scheduler.InitialNoiseSigma;
        Assert.Equal(start + (expected * -scheduler.Sigmas[0]), final[0], 3);
    }

    [Fact]
    public void The_same_seed_draws_the_same_noise_and_a_different_one_does_not()
    {
        var first = new LatentNoise(LatentNoise.SeedFor("B073WXYZ01")).Normal(64);
        var again = new LatentNoise(LatentNoise.SeedFor("B073WXYZ01")).Normal(64);
        var other = new LatentNoise(LatentNoise.SeedFor("B08JKLM202")).Normal(64);

        Assert.Equal(first, again);
        Assert.NotEqual(first, other);
    }

    /// <summary>
    /// The seed is derived from the product id with a hash written out, because
    /// `string.GetHashCode` is randomised per process — with it, the same command
    /// would draw a different picture every time it ran.
    /// </summary>
    [Fact]
    public void The_seed_for_a_product_is_the_same_in_every_process()
    {
        Assert.Equal(LatentNoise.SeedFor("B073WXYZ01"), LatentNoise.SeedFor("B073WXYZ01"));
        Assert.NotEqual(LatentNoise.SeedFor("B073WXYZ01"), LatentNoise.SeedFor("B073WXYZ02"));
    }

    [Fact]
    public void The_noise_is_standard_normal()
    {
        var values = new LatentNoise(12345).Normal(1_000_000);

        var mean = values.Average();
        var deviation = Math.Sqrt(values.Average(value => (value - mean) * (value - mean)));

        Assert.InRange(mean, -0.005, 0.005);
        Assert.InRange(deviation, 0.995, 1.005);
    }
}
