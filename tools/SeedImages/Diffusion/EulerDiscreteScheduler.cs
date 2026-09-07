using System.Text.Json;

namespace ElGuerre.Tendero.SeedImages;

/// <summary>
/// The denoising schedule, and the only piece of the pipeline that is pure
/// arithmetic.
///
/// **Euler discrete, and not by preference: it is what the export declares.**
/// `scheduler/scheduler_config.json` says `EulerDiscreteScheduler`, `scaled_linear`
/// betas from 0.00085 to 0.012, `epsilon` prediction, `leading` spacing and a
/// `steps_offset` of one. The ancestral variants inject fresh noise at every step,
/// which couples the schedule to the random generator and makes "is it the
/// scheduler or the seed?" a question nobody can answer; DPM++ carries a
/// second-order term and a cache of the previous prediction, which is more state
/// to be quietly wrong about for a difference nobody could pick out of a flat
/// illustration at thirty steps.
///
/// The step is one line, and it is the whole method:
///
///     latents += prediction * (sigma[i + 1] - sigma[i])
///
/// The textbook form goes through a predicted clean sample and a derivative,
/// `d = (x - (x - sigma * eps)) / sigma`, and `d` reduces to `eps` exactly. So no
/// division by sigma ever happens and there is no hazard as sigma reaches zero.
/// </summary>
public sealed class EulerDiscreteScheduler
{
    private readonly float[] _sigmas;
    private readonly float[] _timesteps;

    private EulerDiscreteScheduler(float[] trainingSigmas, float[] sigmas, float[] timesteps)
    {
        TrainingSigmas = trainingSigmas;
        _sigmas = sigmas;
        _timesteps = timesteps;
    }

    /// <summary>The full thousand-rung ladder the model was trained on. Exposed
    /// because its two ends are known constants, and asserting them catches a
    /// wrong beta formula with no model in sight.</summary>
    public IReadOnlyList<float> TrainingSigmas { get; }

    /// <summary>One rung per step, plus the zero the last step lands on.</summary>
    public IReadOnlyList<float> Sigmas => _sigmas;

    /// <summary>What to hand the UNet as its `timestep` input, in order.</summary>
    public IReadOnlyList<float> Timesteps => _timesteps;

    /// <summary>
    /// How loud the starting noise has to be. For `leading` and `linspace`
    /// spacing this is the hypotenuse of the top sigma rather than the sigma
    /// itself — the schedule's own convention, and getting it wrong gives a
    /// picture that is either plastic or grainy with no other symptom.
    /// </summary>
    public float InitialNoiseSigma => MathF.Sqrt((_sigmas[0] * _sigmas[0]) + 1f);

    public static EulerDiscreteScheduler FromConfig(string schedulerConfigPath, int steps)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(schedulerConfigPath));
        var root = document.RootElement;

        float Read(string name, float fallback) =>
            root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
                ? value.GetSingle()
                : fallback;

        return Create(
            steps,
            Read("beta_start", 0.00085f),
            Read("beta_end", 0.012f),
            (int)Read("num_train_timesteps", 1000),
            (int)Read("steps_offset", 1));
    }

    public static EulerDiscreteScheduler Create(
        int steps, float betaStart, float betaEnd, int trainTimesteps, int stepsOffset)
    {
        // "scaled_linear": the betas are linear in their SQUARE ROOTS, which is
        // the detail that separates this schedule from the plain linear one and
        // moves both ends of the ladder if you get it backwards.
        var alphaBar = 1f;
        var training = new float[trainTimesteps];

        var rootStart = MathF.Sqrt(betaStart);
        var rootEnd = MathF.Sqrt(betaEnd);

        for (var index = 0; index < trainTimesteps; index++)
        {
            var root = rootStart + ((rootEnd - rootStart) * index / (trainTimesteps - 1));
            var beta = root * root;

            alphaBar *= 1f - beta;
            training[index] = MathF.Sqrt((1f - alphaBar) / alphaBar);
        }

        // "leading" spacing: even strides from zero, reversed, plus the offset.
        var stride = trainTimesteps / steps;
        var timesteps = new float[steps];

        for (var step = 0; step < steps; step++)
            timesteps[step] = (MathF.Round((steps - 1 - step) * (float)stride) + stepsOffset);

        var sigmas = new float[steps + 1];

        for (var step = 0; step < steps; step++)
            sigmas[step] = Interpolate(training, timesteps[step]);

        // The ground. The last step lands here, which is what makes the whole
        // walk telescope back to the latent it started from.
        sigmas[steps] = 0f;

        return new EulerDiscreteScheduler(training, sigmas, timesteps);
    }

    /// <summary>
    /// What the UNet wants to see rather than what the walk is carrying. At the
    /// bottom of the ladder the divisor is one, so this is the identity — which
    /// is the assertion that catches an implementation that divided by sigma.
    /// </summary>
    public float[] ScaleModelInput(float[] latents, int step)
    {
        var sigma = _sigmas[step];
        var divisor = MathF.Sqrt((sigma * sigma) + 1f);

        var scaled = new float[latents.Length];

        for (var index = 0; index < latents.Length; index++)
            scaled[index] = latents[index] / divisor;

        return scaled;
    }

    /// <summary>One rung down the ladder.</summary>
    public float[] Step(float[] latents, float[] noisePrediction, int step)
    {
        var delta = _sigmas[step + 1] - _sigmas[step];

        var next = new float[latents.Length];

        for (var index = 0; index < latents.Length; index++)
            next[index] = latents[index] + (noisePrediction[index] * delta);

        return next;
    }

    /// <summary>
    /// The training ladder is defined at whole timesteps and the schedule asks for
    /// values between them, so the rung is read off the line joining its
    /// neighbours — `interpolation_type: "linear"`, as the config says.
    /// </summary>
    private static float Interpolate(float[] training, float timestep)
    {
        if (timestep <= 0f)
            return training[0];

        if (timestep >= training.Length - 1)
            return training[^1];

        var lower = (int)MathF.Floor(timestep);
        var fraction = timestep - lower;

        return training[lower] + ((training[lower + 1] - training[lower]) * fraction);
    }
}
