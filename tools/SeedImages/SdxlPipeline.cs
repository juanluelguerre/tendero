using System.Diagnostics;

namespace ElGuerre.Tendero.SeedImages;

/// <summary>What one product cost to draw.</summary>
public sealed record GeneratedImage(
    string ItemId, byte[] Webp, int Quality, SafeAreaProbe.Result SafeArea,
    string Background, TimeSpan Took);

/// <summary>
/// The whole of Stable Diffusion XL, in phases, on one card with eight
/// gigabytes.
///
/// **Phases and not per-image sessions, and the reason is arithmetic.** In fp16
/// the UNet is about five gigabytes, the two text towers about 1.6 and the
/// decoder 0.2 — nearly seven before a single activation exists. They cannot all
/// be open at once, so the run is: encode every prompt and close the towers, open
/// the UNet and walk every picture, close it, open the decoder and decode them
/// all. Opening a five gigabyte session costs real seconds, so it is paid once
/// per batch rather than once per image, and what is held in between is 256 KB of
/// latent per product in ordinary memory.
/// </summary>
public sealed class SdxlPipeline(
    string models, string provider, int steps, float guidance, ulong? rngSeed = null, int size = 1024)
    : IIllustrationGenerator
{
    /// <summary>What drew it, for the report.</summary>
    public string Name => $"sdxl · {size}px · {steps} steps";

    /// <summary>**True, and it is the local adapter's advantage.** The noise is
    /// seeded from the item id, so redrawing a product reproduces it exactly
    /// while the prompt is unchanged.</summary>
    public bool IsDeterministic => true;

    public void Dispose() { }

    /// <summary>
    /// **The resolution is a sharpness decision, not a size one.**
    ///
    /// The file is 1024, which is where SDXL is trained, so the default costs
    /// nothing and enlarges nothing. Generating ABOVE it buys detail the
    /// post-processing then throws away when it insets the object, and it costs
    /// roughly four times the wall clock at 1280 — so it is a flag and not a
    /// default. SDXL also starts duplicating handles and limbs well above its
    /// training resolution, which is a risk the tiling detector already watches
    /// for.
    /// </summary>
    private int LatentSize => size / 8;

    private const int Channels = 4;

    /// <summary>How many seeds to try before accepting whatever came out. Three,
    /// because each one costs a walk and the point is to spare a person the
    /// looking, not to search until perfect.</summary>
    private const int MaxAttempts = 3;

    private static string Component(string models, string name) =>
        Path.Combine(models, name, "model.onnx");

    public IReadOnlyList<GeneratedImage> Draw(
        IReadOnlyList<(string ItemId, string Prompt)> jobs, string negative, Action<string> say)
    {
        var scheduler = EulerDiscreteScheduler.FromConfig(
            Path.Combine(models, "scheduler", "scheduler_config.json"), steps);

        // Per image and not per run. The first version started every clock at
        // once, so a batch of five reported five times "370 seconds" -- which is
        // how long the BATCH had been going, and made the marginal cost of an
        // image look five times what it is.
        var clocks = jobs.ToDictionary(job => job.ItemId, _ => new Stopwatch());

        // --- Phase A: the words -------------------------------------------
        say($"encoding {jobs.Count} prompt(s)...");

        var conditioning = new Dictionary<string, TextConditioning>();

        {
            var first = ClipTokenizer.Load(Path.Combine(models, "tokenizer"));
            var second = ClipTokenizer.Load(Path.Combine(models, "tokenizer_2"));

            using var lowTower = new OnnxTextTower(
                OnnxSession.Open(Component(models, "text_encoder"), provider),
                width: 768, hiddenLayer: "hidden_states.11", longIds: false);

            using var highTower = new OnnxTextTower(
                OnnxSession.Open(Component(models, "text_encoder_2"), provider),
                width: 1280, hiddenLayer: "hidden_states.31", longIds: true);

            // Tokenized ONCE PER TOWER, because the two pad with different
            // tokens. One set of ids fed to both is wrong for one of them, in
            // every image, with nothing to say so.
            foreach (var (itemId, prompt) in jobs)
            {
                // Both rows of the guidance batch must be the same length, so the
                // window count is whatever the LONGER of the two prompts needs
                // and the shorter one is padded with empty windows.
                var windows = Math.Max(
                    first.WindowsNeeded(prompt), first.WindowsNeeded(negative));

                conditioning[itemId] = TextConditioning.Build(
                    lowTower,
                    new TextConditioning.PromptTokens(
                        first.EncodeWindows(negative, windows), first.EncodeWindows(prompt, windows)),
                    highTower,
                    new TextConditioning.PromptTokens(
                        second.EncodeWindows(negative, windows), second.EncodeWindows(prompt, windows)));
            }
        }

        // --- Phase B: the walk, and the decode that judges it -------------
        //
        // The decoder is opened alongside the UNet, which the arithmetic allows:
        // five gigabytes and two tenths, and their activations peak at different
        // moments. It has to be, because whether a walk produced a product or a
        // tiled sheet of products cannot be known until it is decoded -- and
        // knowing is what lets the tool try another seed instead of a person
        // having to look at ninety-two pictures.
        List<GeneratedImage> images = [];

        {
            say($"opening the UNet ({steps} steps, guidance {guidance})...");

            using var unet = new OnnxUnet(
                OnnxSession.Open(Component(models, "unet"), provider), LatentSize);

            using var decoder = new OnnxVaeDecoder(
                OnnxSession.Open(Component(models, "vae_decoder"), provider), LatentSize);

            var timeIds = TimeIds.For(size, batch: 2);

            foreach (var (itemId, _) in jobs)
            {
                clocks[itemId].Start();

                byte[] rgb = [];
                var drawn = 0;
                var attempt = 0;

                while (true)
                {
                    // A different seed per attempt. The first is the product's
                    // own, so a run that needs no retry reproduces exactly.
                    var seed = rngSeed ?? LatentNoise.SeedFor(itemId);
                    var noise = new LatentNoise(seed + (ulong)attempt)
                        .Normal(Channels * LatentSize * LatentSize);

                    say($"  {itemId}{(attempt > 0 ? $"  (seed {attempt + 1})" : string.Empty)}");

                    var latents = UnetLoop.Run(
                        scheduler, unet, noise, conditioning[itemId], timeIds, guidance,
                        onStep: (done, total) => Console.Write($"\r    step {done}/{total}   "));

                    Console.WriteLine();

                    rgb = decoder.Decode(latents, out drawn);

                    if (!SafeAreaProbe.Measure(rgb, drawn).LooksTiled(drawn) || ++attempt >= MaxAttempts)
                        break;

                    say("    ink reaches every edge, which is a pattern and not a product. Trying another seed.");
                }

                clocks[itemId].Stop();

                images.Add(Finished.From(itemId, rgb, drawn, clocks[itemId].Elapsed));
            }
        }

        return images;
    }
}
