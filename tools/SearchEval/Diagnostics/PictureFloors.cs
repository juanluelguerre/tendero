using System.Globalization;
using System.Text;
using ElGuerre.Tendero.Search.Evaluation;
using ElGuerre.Tendero.Search.Images;
using SkiaSharp;

namespace ElGuerre.Tendero.SearchEval.Diagnostics;

/// <summary>
/// What a floor could be chosen from, and nothing more: how near the nearest
/// picture is when the answer exists and when it does not.
///
/// A nearest neighbour always exists, so a visual search with no floor answers a
/// photograph of anything with a full row of products. A floor is only as good
/// as the two distributions it separates, and this prints them — for a picture
/// asked with, and for words — without choosing: the number is the author's to
/// set (<c>Search:Images</c>), with these beside it.
///
/// **Its limit is written into its output.** The catalogue's own pictures tell
/// how near *another product* is; they cannot tell how near a photograph of
/// something the shop does not sell would be, because the seed has none. The
/// shapes drawn here — a blank sheet, noise, a gradient, a page of text — are a
/// lower bound for "nothing", not a sample of what shoppers photograph.
/// </summary>
internal static class PictureFloors
{
    public static async Task<string> RenderAsync(
        IImageEmbeddingGenerator model, IPictureSearch pictures, HeldPictures held,
        IReadOnlyList<GoldenSet> goldenSets, IReadOnlyDictionary<string, string> corpus, CancellationToken ct)
    {
        var report = new StringBuilder();
        report.AppendLine("## What a floor would separate");
        report.AppendLine();
        report.AppendLine("Cosine similarity of the nearest catalogue picture. Not gated; printed to choose `Search:Images` floors from.");
        report.AppendLine();
        report.AppendLine("| Asked with | n | Least | A tenth below | Median | A tenth above | Most |");
        report.AppendLine("|---|---|---|---|---|---|---|");

        // A catalogue picture as the question. Its own point answers at 1, so the
        // second product is the nearest OTHER one: what "a similar product" scores.
        List<double> others = [];
        var embedding = new System.Diagnostics.Stopwatch();
        foreach (var (_, bytes) in held.All)
        {
            embedding.Start();
            var vector = await model.EmbedPictureAsync(bytes, ct);
            embedding.Stop();

            var near = await pictures.NearestAsync(vector, 2, ct: ct);
            if (near.Count > 1)
                others.Add(near[1].Similarity);
        }

        report.AppendLine(Row("a catalogue picture — its nearest other product", others));
        var perPicture = held.All.Count == 0 ? 0 : embedding.Elapsed.TotalMilliseconds / held.All.Count;

        List<double> nothing = [];
        foreach (var shape in Shapes())
            if (await pictures.NearestAsync(await model.EmbedPictureAsync(shape, ct), 1, ct: ct) is [var nearest, ..])
                nothing.Add(nearest.Similarity);

        report.AppendLine(Row("a drawn shape that is no product (blank, noise, gradient, text)", nothing));

        var (texts, asking) = (0, new System.Diagnostics.Stopwatch());

        foreach (var golden in goldenSets)
        {
            List<double> right = [], wrong = [], notSold = [];

            foreach (var query in golden.Queries)
            {
                asking.Start();
                var asked = await model.EmbedTextAsync(query.Query, ct);
                asking.Stop();
                texts++;

                if (await pictures.NearestAsync(asked, 1, ct: ct) is not [var first, ..])
                    continue;

                var grade = corpus.TryGetValue(first.ProductId, out var externalId)
                    ? query.ToRelevanceMap().GetValueOrDefault(externalId)
                    : 0;

                if (QueryKinds.IsNoAnswer(query.Kind))
                    notSold.Add(first.Similarity);
                else if (grade >= 2)
                    right.Add(first.Similarity);
                else
                    wrong.Add(first.Similarity);
            }

            report.AppendLine(Row($"words ({golden.Culture}) — the first picture is a right answer", right));
            report.AppendLine(Row($"words ({golden.Culture}) — the first picture is not", wrong));
            report.AppendLine(Row($"words ({golden.Culture}) — for something the shop does not sell", notSold));
        }

        report.AppendLine();
        report.AppendLine(string.Create(CultureInfo.InvariantCulture,
            $"Embedding, on this machine's CPU: {perPicture:F0} ms a picture over {held.All.Count} (decoding and resizing included), " +
            $"{(texts == 0 ? 0 : asking.Elapsed.TotalMilliseconds / texts):F0} ms a text over {texts}."));
        report.AppendLine();
        report.AppendLine(
            "The seed holds no photograph of something the shop does not sell, so the picture floor's lower " +
            "distribution is the drawn shapes: a lower bound for *nothing*, not what a shopper photographs.");

        return report.ToString();
    }

    private static string Row(string name, List<double> values)
    {
        if (values.Count == 0)
            return $"| {name} | 0 | – | – | – | – | – |";

        values.Sort();
        double At(double share) => values[(int)Math.Round(share * (values.Count - 1))];
        string Said(double value) => value.ToString("0.000", CultureInfo.InvariantCulture);

        return $"| {name} | {values.Count} | {Said(values[0])} | {Said(At(0.1))} | {Said(At(0.5))} | {Said(At(0.9))} | {Said(values[^1])} |";
    }

    /// <summary>Four pictures of nothing: a blank sheet, noise, a gradient, and a page of words.</summary>
    private static IEnumerable<byte[]> Shapes()
    {
        yield return Drawn(canvas => canvas.Clear(SKColors.White));

        yield return Drawn(canvas =>
        {
            // The same noise on every run: a seed, not the clock.
            var random = new Random(13);
            using var paint = new SKPaint();
            for (var y = 0; y < 256; y += 4)
            {
                for (var x = 0; x < 256; x += 4)
                {
                    paint.Color = new SKColor((byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256));
                    canvas.DrawRect(x, y, 4, 4, paint);
                }
            }
        });

        yield return Drawn(canvas =>
        {
            using var shader = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(256, 256), [SKColors.Navy, SKColors.Orange], SKShaderTileMode.Clamp);
            using var paint = new SKPaint { Shader = shader };
            canvas.DrawRect(0, 0, 256, 256, paint);
        });

        yield return Drawn(canvas =>
        {
            canvas.Clear(SKColors.White);
            using var font = new SKFont(SKTypeface.Default, 18);
            using var paint = new SKPaint { Color = SKColors.Black, IsAntialias = true };
            for (var line = 0; line < 10; line++)
                canvas.DrawText("Terms of the agreement, clause " + (line + 1), 8, 24 + (line * 24), SKTextAlign.Left, font, paint);
        });
    }

    private static byte[] Drawn(Action<SKCanvas> draw)
    {
        using var bitmap = new SKBitmap(new SKImageInfo(256, 256, SKColorType.Rgba8888, SKAlphaType.Opaque));
        using (var canvas = new SKCanvas(bitmap))
            draw(canvas);
        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        return encoded.ToArray();
    }
}
