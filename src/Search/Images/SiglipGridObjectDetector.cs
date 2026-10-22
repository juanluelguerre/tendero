using SkiaSharp;

namespace ElGuerre.Tendero.Search.Images;

/// <summary>
/// The cheap reference among the detectors measured (the camera search spec,
/// CS-2): no detector at all, but the visual search's own model asked about parts
/// of the picture. The picture is cut into overlapping cells at three sizes —
/// the whole, a three-by-three of halves, a five-by-five of thirds — each cell is
/// embedded, and each kind takes the cell its words are most like.
///
/// **It places nothing.** A box is a cell of the grid, never the object's outline,
/// and it costs a pass of the vision tower a cell — thirty-five a picture, about
/// four seconds on the CPU at the 121 ms a pass measured in phase 13a. It is here
/// to be the number a real detector has to beat, and needs no new dependency.
///
/// A score is SigLIP's own probability that the words describe the cell:
/// <c>sigmoid(scale · cosine + bias)</c>, with the scale and the bias the pinned
/// revision learned — read from its weights, not chosen.
/// </summary>
public sealed class SiglipGridObjectDetector(IImageEmbeddingGenerator embeddings) : IObjectDetector
{
    public const string Key = "siglip-grid";

    /// <summary><c>exp(logit_scale)</c> of google/siglip2-base-patch16-224 at revision 75de2d55, whose logit_scale is 4.724453.</summary>
    internal const double Scale = 112.6809;

    /// <summary>The same revision's logit_bias.</summary>
    internal const double Bias = -16.771725;

    /// <summary>The sentence SigLIP was evaluated with: the words alone are a weaker question.</summary>
    internal static string Asked(string words) => $"This is a photo of a {words}.";

    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, ReadOnlyMemory<float>> asked = new();

    public string Name => Key;

    public bool IsConfigured => embeddings.IsConfigured;

    public async Task<IReadOnlyList<DetectedObject>> DetectAsync(ReadOnlyMemory<byte> picture, IReadOnlyList<ObjectKind> kinds, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var (rgb, width, height) = SiglipPicture.Upright(picture.Span);
        if (kinds.Count == 0)
            return [];

        // The kinds are configuration: their words are embedded once, not once a
        // picture, each alternative on its own; a kind is as like a cell as its
        // likest alternative.
        var words = new List<ReadOnlyMemory<float>[]>(kinds.Count);
        foreach (var kind in kinds)
        {
            var alternatives = new List<ReadOnlyMemory<float>>();
            foreach (var alternative in kind.Alternatives)
            {
                if (!this.asked.TryGetValue(alternative, out var embedded)) this.asked[alternative] = embedded = await embeddings.EmbedTextAsync(Asked(alternative), cancellationToken);
                alternatives.Add(embedded);
            }

            words.Add([.. alternatives]);
        }

        // The cell is chosen by the cosine, and only the winner's is turned into a
        // probability: the sigmoid saturates, and two cells at 1.0 would tie.
        var best = Enumerable.Repeat((Box: default(PictureBox), Cosine: Double.NegativeInfinity), kinds.Count).ToArray();
        foreach (var cell in Cells(width, height))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var seen = await embeddings.EmbedPictureAsync(Png(rgb, width, cell), cancellationToken);
            for (var at = 0; at < kinds.Count; at++)
            {
                var cosine = words[at].Max(alternative => Cosine(seen.Span, alternative.Span));
                if (cosine > best[at].Cosine)
                    best[at] = (cell, cosine);
            }
        }

        return [.. kinds.Select((kind, at) => new DetectedObject(kind, best[at].Box, Probability(best[at].Cosine)))];
    }

    /// <summary>The whole picture, then halves and thirds at half a cell's stride, so an object across a line is still inside one cell.</summary>
    internal static IReadOnlyList<PictureBox> Cells(int width, int height) =>
        [.. from parts in (int[])[1, 2, 3]
            let cellWidth = width / parts
            let cellHeight = height / parts
            let steps = (2 * parts) - 1
            from row in Enumerable.Range(0, steps)
            from column in Enumerable.Range(0, steps)
            select new PictureBox(
                column == steps - 1 ? width - cellWidth : column * cellWidth / 2,
                row == steps - 1 ? height - cellHeight : row * cellHeight / 2,
                cellWidth,
                cellHeight)];

    internal static double Probability(double cosine) => 1 / (1 + Math.Exp(-((Scale * cosine) + Bias)));

    private static double Cosine(ReadOnlySpan<float> one, ReadOnlySpan<float> another)
    {
        var dot = 0.0;
        for (var at = 0; at < one.Length; at++)
            dot += one[at] * another[at];
        return dot;
    }

    /// <summary>A cell as a PNG, the bytes the port takes: lossless, so the tower reads the cell's own pixels.</summary>
    private static byte[] Png(byte[] rgb, int width, PictureBox cell)
    {
        using var bitmap = new SKBitmap(new SKImageInfo(cell.Width, cell.Height, SKColorType.Rgba8888, SKAlphaType.Opaque));
        var pixels = bitmap.GetPixelSpan();
        for (var y = 0; y < cell.Height; y++)
        {
            for (var x = 0; x < cell.Width; x++)
            {
                var from = (((cell.Top + y) * width) + cell.Left + x) * 3;
                var to = ((y * cell.Width) + x) * 4;
                pixels[to] = rgb[from];
                pixels[to + 1] = rgb[from + 1];
                pixels[to + 2] = rgb[from + 2];
                pixels[to + 3] = 255;
            }
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        return encoded.ToArray();
    }
}
