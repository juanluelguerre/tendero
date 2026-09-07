namespace ElGuerre.Tendero.SeedImages;

/// <summary>
/// The model that answers the only question in the loop: *given this noise and
/// this prompt, which part of it is noise?*
///
/// It never draws anything. It points at what to remove, and the scheduler
/// decides how much of that to remove this time round.
/// </summary>
public interface IUnet : IDisposable
{
    /// <param name="sample">[2, 4, h, w] flattened — the same latent twice, once
    /// for each row of the conditioning.</param>
    /// <returns>[2, 4, h, w] flattened: the noise prediction for each row.</returns>
    float[] Predict(float[] sample, float timestep, TextConditioning text, float[] timeIds);
}

/// <summary>
/// The denoising walk.
///
/// The whole of diffusion, once the vocabulary is out of the way: start from
/// noise, ask the model what part of it is noise, remove a measured slice of
/// that, and go round again. Thirty times, and a picture is left behind.
/// </summary>
public static class UnetLoop
{
    /// <summary>
    /// Runs the walk and returns the final latent.
    /// </summary>
    /// <param name="guidance">
    /// How hard to push away from the negative prompt. The model is asked twice —
    /// once conditioned on the prompt and once on its opposite — and the
    /// difference between the two answers is amplified by this number. **That is
    /// the entire mechanism behind a negative prompt**, and the reason writing
    /// "no text" in the positive one does nothing at all.
    ///
    /// At 1.0 the amplification is nil and the conditional answer is used as it
    /// came, which is what the test asserts.
    /// </param>
    public static float[] Run(
        EulerDiscreteScheduler scheduler,
        IUnet unet,
        float[] noise,
        TextConditioning text,
        float[] timeIds,
        float guidance,
        Action<int, int>? onStep = null)
    {
        // The latent begins as noise at the top of the ladder. Not noise plus
        // something: there is nothing underneath yet, and the picture is what the
        // walk leaves behind.
        var latents = new float[noise.Length];

        for (var index = 0; index < noise.Length; index++)
            latents[index] = noise[index] * scheduler.InitialNoiseSigma;

        var steps = scheduler.Timesteps.Count;

        for (var step = 0; step < steps; step++)
        {
            var scaled = scheduler.ScaleModelInput(latents, step);

            // One latent, two rows: the model sees the same picture asked about
            // in two different ways.
            var batch = new float[scaled.Length * 2];
            scaled.CopyTo(batch, 0);
            scaled.CopyTo(batch, scaled.Length);

            var prediction = unet.Predict(batch, scheduler.Timesteps[step], text, timeIds);

            var guided = Guide(prediction, guidance, latents.Length);

            latents = scheduler.Step(latents, guided, step);

            onStep?.Invoke(step + 1, steps);
        }

        return latents;
    }

    /// <summary>
    /// Classifier-free guidance: the unconditional answer, plus the amplified
    /// difference towards the conditional one. Row zero is the negative prompt,
    /// which is why the order of the conditioning rows is a tested property and
    /// not a convention.
    /// </summary>
    private static float[] Guide(float[] prediction, float guidance, int length)
    {
        var guided = new float[length];

        for (var index = 0; index < length; index++)
        {
            var unconditional = prediction[index];
            var conditional = prediction[length + index];

            guided[index] = unconditional + (guidance * (conditional - unconditional));
        }

        return guided;
    }
}
