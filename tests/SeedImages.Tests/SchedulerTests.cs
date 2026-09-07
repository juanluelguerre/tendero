using ElGuerre.Tendero.SeedImages;
using Xunit;

namespace ElGuerre.Tendero.SeedImages.Tests;

/// <summary>
/// The denoising schedule, which is pure arithmetic and therefore the one part of
/// the pipeline that can be wrong in a way a test catches instead of a way an
/// image hints at.
///
/// Everything here runs with no GPU, no model and no weights. That is the whole
/// reason the pipeline is cut where it is cut: a scheduler with the wrong sigma
/// ladder produces noise, or fog, and says nothing about which.
/// </summary>
public sealed class SchedulerTests
{
    /// <summary>
    /// The values the export's own `scheduler_config.json` declares, read once
    /// and written here so these tests need no model on disk.
    /// </summary>
    private static EulerDiscreteScheduler Scheduler(int steps) =>
        EulerDiscreteScheduler.Create(steps, betaStart: 0.00085f, betaEnd: 0.012f, trainTimesteps: 1000, stepsOffset: 1);

    /// <summary>
    /// **The landmark check.** A `scaled_linear` schedule from 0.00085 to 0.012
    /// over a thousand steps has a known first and last sigma, and those two
    /// numbers are quoted everywhere the maths is discussed. Getting the beta
    /// formula subtly wrong -- squaring the wrong thing, or interpolating the
    /// betas instead of their roots -- moves them, and moves nothing else that a
    /// person would notice until the images come out fogged.
    /// </summary>
    [Fact]
    public void The_noise_schedule_runs_between_the_two_landmark_values()
    {
        var scheduler = Scheduler(30);

        Assert.Equal(0.0292, scheduler.TrainingSigmas[0], 3);
        Assert.Equal(14.6146, scheduler.TrainingSigmas[^1], 3);
    }

    /// <summary>
    /// One rung per step, plus the zero the last step lands on. An off-by-one
    /// here is the difference between finishing the denoising and stopping one
    /// short of it, which looks like a model that is slightly bad at everything.
    /// </summary>
    [Theory]
    [InlineData(4)]
    [InlineData(20)]
    [InlineData(30)]
    public void The_ladder_has_one_more_rung_than_steps_and_ends_on_the_ground(int steps)
    {
        var scheduler = Scheduler(steps);

        Assert.Equal(steps + 1, scheduler.Sigmas.Count);
        Assert.Equal(steps, scheduler.Timesteps.Count);
        Assert.Equal(0f, scheduler.Sigmas[^1]);

        for (var rung = 1; rung < scheduler.Sigmas.Count; rung++)
            Assert.True(
                scheduler.Sigmas[rung] < scheduler.Sigmas[rung - 1],
                $"sigma {rung} ({scheduler.Sigmas[rung]}) is not below sigma {rung - 1} ({scheduler.Sigmas[rung - 1]}).");
    }

    /// <summary>
    /// **The step count is part of the look, and this test exists to say so.**
    ///
    /// `leading` spacing takes even strides from zero and reverses them, so the
    /// top rung is `(steps - 1) * (1000 / steps) + 1` — which is not 999, and
    /// which MOVES with the step count. Measured on this schedule: the starting
    /// noise level is 11.07 at twenty steps, 11.52 at thirty and 13.16 at fifty,
    /// a nineteen per cent spread.
    ///
    /// It reads like a bug and it is the schedule doing what its own config says
    /// (`timestep_spacing: "leading"`, `steps_offset: 1`); the `trailing` spacing
    /// that later diffusers versions default to exists precisely because of this.
    ///
    /// The consequence is a rule rather than a fix: **the hundred illustrations
    /// have to be generated at one step count.** Regenerating number 47 later
    /// with a different `--steps` gives a different picture, not a better one,
    /// and a catalogue where six images came from a different ladder does not
    /// look like a catalogue.
    /// </summary>
    [Theory]
    [InlineData(20, 951)]
    [InlineData(30, 958)]
    [InlineData(50, 981)]
    public void The_top_of_the_ladder_moves_with_the_step_count(int steps, int topTimestep)
    {
        var scheduler = Scheduler(steps);

        Assert.Equal(topTimestep, (int)scheduler.Timesteps[0]);
        Assert.Equal(1, (int)scheduler.Timesteps[^1]);
    }

    [Fact]
    public void More_steps_start_from_a_noisier_place_which_is_why_the_count_is_fixed()
    {
        Assert.True(
            Scheduler(20).InitialNoiseSigma < Scheduler(50).InitialNoiseSigma,
            "leading spacing puts the top rung higher as the step count grows; if that stopped "
            + "being true the schedule changed underneath us.");
    }

    /// <summary>
    /// `ScaleModelInput` divides by the hypotenuse of the sigma, and at the
    /// bottom of the ladder -- where sigma is zero -- that is the identity. A
    /// naive implementation divides by sigma somewhere and finds out here.
    /// </summary>
    [Fact]
    public void Scaling_the_input_at_the_bottom_of_the_ladder_changes_nothing()
    {
        var scheduler = Scheduler(4);
        float[] latents = [1f, -2f, 0.5f, 100f];

        var scaled = scheduler.ScaleModelInput(latents, scheduler.Sigmas.Count - 1);

        Assert.Equal(latents, scaled);
    }

    [Fact]
    public void Scaling_the_input_divides_by_the_hypotenuse_of_the_sigma()
    {
        var scheduler = Scheduler(30);
        float[] latents = [4f];

        var scaled = scheduler.ScaleModelInput(latents, 0);
        var expected = 4f / MathF.Sqrt((scheduler.Sigmas[0] * scheduler.Sigmas[0]) + 1f);

        Assert.Equal(expected, scaled[0], 5);
    }

    /// <summary>
    /// **The one that proves the whole thing, and it needs no model.**
    ///
    /// Start from a latent, add sigma-scaled noise the way the pipeline does, then
    /// run every step with a denoiser that returns exactly the noise it was given.
    /// The Euler update is `x += eps * (sigma[i+1] - sigma[i])`, so the sum
    /// telescopes: every step cancels against the next and the last sigma is zero,
    /// leaving the latent you started from.
    ///
    /// One assertion, and it validates the sigma ladder, the initial scaling, the
    /// direction of the step, the accumulation and the off-by-one in `sigma[i+1]`
    /// all at once. If this is wrong, something between the schedule and the loop
    /// is wrong, and you know it before downloading ten gigabytes.
    /// </summary>
    [Fact]
    public void A_denoiser_that_returns_the_noise_it_was_given_gives_back_where_it_started()
    {
        var scheduler = Scheduler(12);

        float[] clean = [0.25f, -1.5f, 3f, 0f];
        float[] noise = [1f, -0.5f, 2f, 0.75f];

        // What the pipeline does before the first step: the latent is pure noise
        // scaled to the top of the ladder. Here there is a signal underneath it so
        // the assertion has something to be about.
        var latents = clean
            .Zip(noise, (signal, epsilon) => signal + (epsilon * scheduler.Sigmas[0]))
            .ToArray();

        for (var step = 0; step < scheduler.Timesteps.Count; step++)
            latents = scheduler.Step(latents, noise, step);

        for (var index = 0; index < clean.Length; index++)
            Assert.Equal(clean[index], latents[index], 4);
    }
}
