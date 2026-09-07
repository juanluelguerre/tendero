using System.Diagnostics;

namespace ElGuerre.Tendero.SeedImages;

/// <summary>What one product cost to draw.</summary>
public sealed record GeneratedImage(
    string ItemId, byte[] Webp, int Quality, SafeAreaProbe.Result SafeArea, TimeSpan Took);

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
public sealed class SdxlPipeline(string models, string provider, int steps, float guidance, ulong? rngSeed = null)
{
    /// <summary>SDXL works on a grid eight times smaller than the picture.</summary>
    private const int LatentSize = 1024 / 8;

    private const int Channels = 4;

    private static string Component(string models, string name) =>
        Path.Combine(models, name, "model.onnx");

    public IReadOnlyList<GeneratedImage> Generate(
        IReadOnlyList<(string ItemId, string Prompt)> jobs, string negative, Action<string> say)
    {
        var scheduler = EulerDiscreteScheduler.FromConfig(
            Path.Combine(models, "scheduler", "scheduler_config.json"), steps);

        var clocks = jobs.ToDictionary(job => job.ItemId, _ => Stopwatch.StartNew());

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

        // --- Phase B: the walk --------------------------------------------
        var latents = new Dictionary<string, float[]>();

        {
            say($"opening the UNet ({steps} steps, guidance {guidance})...");

            using var unet = new OnnxUnet(
                OnnxSession.Open(Component(models, "unet"), provider), LatentSize);

            var timeIds = TimeIds.For(1024, batch: 2);

            foreach (var (itemId, _) in jobs)
            {
                // The seed is the product's own unless one is given, so
                // regenerating a product reproduces it. An override exists
                // because the COMPOSITION lives in the noise more than in the
                // words: three prompt rewrites gave the same layout three times,
                // and a different seed gave a different one immediately.
                var noise = new LatentNoise(rngSeed ?? LatentNoise.SeedFor(itemId))
                    .Normal(Channels * LatentSize * LatentSize);

                say($"  {itemId}");

                latents[itemId] = UnetLoop.Run(
                    scheduler, unet, noise, conditioning[itemId], timeIds, guidance,
                    onStep: (done, total) => Console.Write($"\r    step {done}/{total}   "));

                Console.WriteLine();
            }
        }

        // --- Phase C: the picture ------------------------------------------
        say("decoding...");

        using var decoder = new OnnxVaeDecoder(
            OnnxSession.Open(Component(models, "vae_decoder"), provider), LatentSize);

        List<GeneratedImage> images = [];

        foreach (var (itemId, _) in jobs)
        {
            var rgb = decoder.Decode(latents[itemId], out var size);
            var safeArea = SafeAreaProbe.Probe(rgb, size);
            var webp = ImageNormaliser.ToWebp(rgb, size, out var quality);

            clocks[itemId].Stop();

            images.Add(new GeneratedImage(itemId, webp, quality, safeArea, clocks[itemId].Elapsed));
        }

        return images;
    }
}
