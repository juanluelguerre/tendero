using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using ElGuerre.Tendero.Search.Contracts;

namespace ElGuerre.Tendero.Search.Images;

/// <summary>
/// SigLIP 2 in this process, on ONNX Runtime (phase 13a, P13-2): the port's
/// first adapter, and the reason there is no Python beside the shop. The two
/// towers are our own export of the maker's weights at the pinned revision
/// (<c>src/AppHost/scripts/prepare-siglip.py</c>, ADR 0031), in a folder this is
/// pointed at; the text's tokenizer is the one held to the maker's, id for id.
///
/// **On the CPU, on purpose.** A picture is one pass of a 93-million-parameter
/// tower, tens of milliseconds, and the catalogue is embedded by the outbox and
/// not by a request; the card is the generative model's.
///
/// **Each tower is loaded when it is first asked for**, not when the process
/// starts: the text tower's vocabulary is a quarter of a million rows and the
/// file is over a gigabyte. A shop that indexes pictures and is never searched
/// by words never pays for it.
/// </summary>
public sealed class SiglipOnnxEmbeddings : IImageEmbeddingGenerator, IDisposable
{
    public const string VisionFile = "vision.onnx";
    public const string TextFile = "text.onnx";

    private readonly Lazy<InferenceSession> vision;
    private readonly Lazy<InferenceSession> text;
    private readonly Lazy<SiglipTextTokenizer> tokenizer;

    /// <param name="folder">Where the export left <see cref="VisionFile"/>, <see cref="TextFile"/> and the tokenizer's vocabulary.</param>
    public SiglipOnnxEmbeddings(string folder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);

        foreach (var file in (string[])[VisionFile, TextFile, SiglipTextTokenizer.FileName])
        {
            if (!File.Exists(Path.Combine(folder, file)))
                throw new FileNotFoundException($"No {file} in '{folder}': run the export (src/AppHost/scripts/prepare-siglip.py).", Path.Combine(folder, file));
        }

        this.vision = new(() => new InferenceSession(Path.Combine(folder, VisionFile)));
        this.text = new(() => new InferenceSession(Path.Combine(folder, TextFile)));
        this.tokenizer = new(() => SiglipTextTokenizer.Load(Path.Combine(folder, SiglipTextTokenizer.FileName)));
    }

    public EmbeddingModelDescriptor Model => EmbeddingModelDescriptor.Siglip2Base;

    public ValueTask<ReadOnlyMemory<float>> EmbedPictureAsync(ReadOnlyMemory<byte> picture, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var pixels = new DenseTensor<float>(SiglipPicture.ToTensor(picture.Span), [1, 3, SiglipPicture.Side, SiglipPicture.Side]);
        return ValueTask.FromResult(Run(this.vision.Value, NamedOnnxValue.CreateFromTensor("pixel_values", pixels)));
    }

    public ValueTask<ReadOnlyMemory<float>> EmbedTextAsync(string text, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);
        cancellationToken.ThrowIfCancellationRequested();

        var ids = new DenseTensor<long>(this.tokenizer.Value.Encode(Normalised(text)), [1, SiglipTextTokenizer.Length]);
        return ValueTask.FromResult(Run(this.text.Value, NamedOnnxValue.CreateFromTensor("input_ids", ids)));
    }

    /// <summary>
    /// The text as it is given to the tokenizer: **lower-cased**. The model's
    /// configuration asks for it and the maker's tokenizer does not do it, so it
    /// was measured (the export, each seed product's name against every seed
    /// picture, 2026-10-08): Spanish names find their own picture first 88 % of
    /// the time lower-cased and 65 % as written; English names 85 % either way;
    /// and a name in capitals 1 %. Invariant, because the repository builds with
    /// no culture data and a shopper's language is not this model's to guess.
    /// </summary>
    internal static string Normalised(string text) => text.ToLowerInvariant();

    private ReadOnlyMemory<float> Run(InferenceSession session, NamedOnnxValue input)
    {
        using var results = session.Run([input]);
        var vector = results[0].AsEnumerable<float>().ToArray();
        if (vector.Length != Model.Dimensions)
            throw new InvalidOperationException($"The model answered {vector.Length} dimensions; {Model.ModelId} has {Model.Dimensions}.");

        // Unit length, once and here: the nearest picture is then the largest dot product.
        var norm = MathF.Sqrt(vector.Sum(value => value * value));
        for (var at = 0; at < vector.Length; at++)
            vector[at] /= norm;

        return vector;
    }

    public void Dispose()
    {
        if (this.vision.IsValueCreated)
            this.vision.Value.Dispose();
        if (this.text.IsValueCreated)
            this.text.Value.Dispose();
    }
}
