using System.Collections.Concurrent;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using SkiaSharp;

namespace ElGuerre.Tendero.Search.Images;

/// <summary>
/// The search box's detector (the camera search, CS-2; ADR 0062): OWLv2, an
/// open-vocabulary detector, in this process on ONNX Runtime. Chosen by the
/// author on 2026-10-19 over SigLIP's grid and the local Qwen with the numbers in
/// <c>docs/analysis/object-detectors.md</c>: it is the one that places objects and
/// whose score can be put a floor under, and it runs on the CPU, queued behind no
/// agent.
///
/// The two networks are our export of the maker's weights at the pinned revision
/// (<c>src/AppHost/scripts/prepare-owlv2.py</c>): the text tower turns each kind's
/// words into a query, and the detector scores every one of its 3,600 patches
/// against every query and draws a box at each. A kind asked as several
/// alternatives is as likely as its likeliest one; each kind keeps its best patch.
/// The queries are embedded once a process, not once a picture: the kinds are
/// configuration.
///
/// **Each network is loaded when it is first asked for**, as SigLIP's are.
/// </summary>
public sealed class Owlv2ObjectDetector : IObjectDetector, IDisposable
{
    public const string Key = "owlv2";
    public const string Model = "google/owlv2-base-patch16-ensemble";

    /// <summary>The maker's commit the export and its reference are of.</summary>
    public const string Revision = "cfd3195ba4ea9592eec887ded089f4c08eff231d";

    public const string DetectorFile = "detector.onnx";
    public const string TextFile = "text.onnx";

    /// <summary>The side of the square the detector reads.</summary>
    internal const int Side = 960;

    private static readonly float[] Mean = [0.48145466f, 0.4578275f, 0.40821073f];
    private static readonly float[] Deviation = [0.26862954f, 0.26130258f, 0.27577711f];

    private readonly Lazy<InferenceSession> detector;
    private readonly Lazy<InferenceSession> text;
    private readonly Lazy<Owlv2TextTokenizer> tokenizer;
    private readonly ConcurrentDictionary<string, float[]> queries = new();

    /// <param name="folder">Where the export left both networks and the tokenizer's files.</param>
    public Owlv2ObjectDetector(string folder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);

        foreach (var file in (string[])[DetectorFile, TextFile, Owlv2TextTokenizer.VocabularyFile, Owlv2TextTokenizer.MergesFile])
        {
            if (!File.Exists(Path.Combine(folder, file)))
                throw new FileNotFoundException($"No {file} in '{folder}': run the export (src/AppHost/scripts/prepare-owlv2.py).", Path.Combine(folder, file));
        }

        this.detector = new(() => Network(Path.Combine(folder, DetectorFile)));
        this.text = new(() => Network(Path.Combine(folder, TextFile)));
        this.tokenizer = new(() => Owlv2TextTokenizer.Load(folder));
    }

    /// <summary>The sentence OWL's queries were trained on.</summary>
    internal static string Asked(string words) => $"a photo of a {words}";

    public string Name => Key;

    private static InferenceSession Network(string path)
    {
        // Half the logical processors, never more than eight: measured for SigLIP (SiglipOnnxEmbeddings.Threads).
        using var options = new SessionOptions { IntraOpNumThreads = SiglipOnnxEmbeddings.Threads };
        return new InferenceSession(path, options);
    }

    public Task<IReadOnlyList<DetectedObject>> DetectAsync(ReadOnlyMemory<byte> picture, IReadOnlyList<ObjectKind> kinds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(kinds);
        cancellationToken.ThrowIfCancellationRequested();
        var (rgb, width, height) = SiglipPicture.Upright(picture.Span);
        if (kinds.Count == 0)
            return Task.FromResult<IReadOnlyList<DetectedObject>>([]);

        // One query a kind's alternative, each embedded once a process.
        var rows = (from kind in kinds from alternative in kind.Alternatives select (Kind: kind, Words: alternative)).ToList();
        Embed([.. rows.Select(row => row.Words).Distinct().Where(words => !this.queries.ContainsKey(words))]);
        var dimension = this.queries[rows[0].Words].Length;
        var embeds = new DenseTensor<float>([1, rows.Count, dimension]);
        for (var row = 0; row < rows.Count; row++)
            this.queries[rows[row].Words].CopyTo(embeds.Buffer.Span.Slice(row * dimension, dimension));

        cancellationToken.ThrowIfCancellationRequested();
        using var results = this.detector.Value.Run(
        [
            NamedOnnxValue.CreateFromTensor("pixel_values", Pixels(rgb, width, height)),
            NamedOnnxValue.CreateFromTensor("query_embeds", embeds),
        ]);
        var logits = results.First(result => result.Name == "logits").AsEnumerable<float>().ToArray();
        var boxes = results.First(result => result.Name == "boxes").AsEnumerable<float>().ToArray();
        var square = Math.Max(width, height);

        var found = new List<DetectedObject>(kinds.Count);
        foreach (var kind in kinds)
        {
            var (patch, logit) = Likeliest(logits, rows.Count, [.. Enumerable.Range(0, rows.Count).Where(row => rows[row].Kind == kind)]);
            var at = patch * 4;
            if (Box(boxes[at], boxes[at + 1], boxes[at + 2], boxes[at + 3], square).ClippedTo(width, height) is { } box)
                found.Add(new DetectedObject(kind, box, 1 / (1 + Math.Exp(-logit))));
        }

        return Task.FromResult<IReadOnlyList<DetectedObject>>(found);
    }

    private void Embed(IReadOnlyList<string> words)
    {
        if (words.Count == 0)
            return;

        var ids = new DenseTensor<long>([words.Count, Owlv2TextTokenizer.Length]);
        for (var at = 0; at < words.Count; at++)
            this.tokenizer.Value.Encode(Asked(words[at])).CopyTo(ids.Buffer.Span.Slice(at * Owlv2TextTokenizer.Length, Owlv2TextTokenizer.Length));

        using var results = this.text.Value.Run([NamedOnnxValue.CreateFromTensor("input_ids", ids)]);
        var embedded = results[0].AsEnumerable<float>().ToArray();
        var dimension = embedded.Length / words.Count;
        for (var at = 0; at < words.Count; at++)
            this.queries[words[at]] = embedded[(at * dimension)..((at + 1) * dimension)];
    }

    /// <summary>The patch and the logit of a kind's likeliest alternative, over logits laid out patch by patch, <paramref name="queries"/> a patch.</summary>
    internal static (int Patch, float Logit) Likeliest(ReadOnlySpan<float> logits, int queries, IReadOnlyList<int> rows)
    {
        var (best, logit) = (0, float.NegativeInfinity);
        for (var patch = 0; patch < logits.Length / queries; patch++)
        {
            foreach (var row in rows)
            {
                if (logits[(patch * queries) + row] > logit)
                    (best, logit) = (patch, logits[(patch * queries) + row]);
            }
        }

        return (best, logit);
    }

    /// <summary>A box as OWL draws it — a centre and a size, in parts of the padded square — in the picture's pixels.</summary>
    internal static PictureBox Box(float centreX, float centreY, float width, float height, int square) => new(
        (int)Math.Round((centreX - (width / 2)) * square),
        (int)Math.Round((centreY - (height / 2)) * square),
        (int)Math.Round(width * square),
        (int)Math.Round(height * square));

    /// <summary>
    /// The picture at the top left of a grey square, scaled to <see cref="Side"/> and
    /// normalised channel first, as the maker's processor does. Skia's linear scaling
    /// with mipmaps stands in for its anti-aliased resize: near, not the same, and
    /// held to it by the fixture's detections.
    /// </summary>
    internal static DenseTensor<float> Pixels(byte[] rgb, int width, int height)
    {
        using var source = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Opaque));
        var from = source.GetPixelSpan();
        for (var at = 0; at < width * height; at++)
        {
            from[at * 4] = rgb[at * 3];
            from[(at * 4) + 1] = rgb[(at * 3) + 1];
            from[(at * 4) + 2] = rgb[(at * 3) + 2];
            from[(at * 4) + 3] = 255;
        }

        var scale = (float)Side / Math.Max(width, height);
        using var square = new SKBitmap(new SKImageInfo(Side, Side, SKColorType.Rgba8888, SKAlphaType.Opaque));
        using (var canvas = new SKCanvas(square))
        {
            canvas.Clear(new SKColor(128, 128, 128));
            using var image = SKImage.FromBitmap(source);
            canvas.DrawImage(image, SKRect.Create(0, 0, width * scale, height * scale), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
        }

        var tensor = new DenseTensor<float>([1, 3, Side, Side]);
        var pixels = square.GetPixelSpan();
        var values = tensor.Buffer.Span;
        for (var at = 0; at < Side * Side; at++)
        {
            for (var channel = 0; channel < 3; channel++)
                values[(channel * Side * Side) + at] = ((pixels[(at * 4) + channel] / 255f) - Mean[channel]) / Deviation[channel];
        }

        return tensor;
    }

    public void Dispose()
    {
        if (this.detector.IsValueCreated)
            this.detector.Value.Dispose();
        if (this.text.IsValueCreated)
            this.text.Value.Dispose();
    }
}
