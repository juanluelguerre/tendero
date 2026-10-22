using System.Text.Json;
using Microsoft.Extensions.AI;
using SkiaSharp;

namespace ElGuerre.Tendero.Search.Images;

/// <summary>
/// A generative model with eyes, asked where the things in a picture are (the
/// camera search spec, CS-2): the shop's local Qwen3.5-4B with its projector,
/// already converted and running for Tino and Steward, so it needs no new
/// dependency. It is one of the candidates measured, not the choice.
///
/// **It composes, so it is asked for JSON** — boxes are made, not chosen among
/// options (ADR 0055) — in thousandths of the picture's width and height, which
/// no resizing on the model's side can change the meaning of.
///
/// **It gives no score.** A generative model names what it believes it sees; how
/// sure it is does not come back with the words. Every box it names scores 1, so
/// a floor separates nothing for it — what the measurement has to say about it.
/// Its limits otherwise are the ones the spec lists: a GPU, the one slot it
/// shares with the agents, and the objects it may invent.
/// </summary>
public sealed partial class VisionModelObjectDetector(IChatClient model) : IObjectDetector
{
    public const string Key = "vision-model";

    public string Name => Key;

    public async Task<IReadOnlyList<DetectedObject>> DetectAsync(ReadOnlyMemory<byte> picture, IReadOnlyList<ObjectKind> kinds, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var (width, height, png) = Png(picture);
        if (kinds.Count == 0)
            return [];

        var question = new ChatMessage(ChatRole.User,
        [
            new DataContent(png, "image/png"),
            new TextContent(Question(kinds)),
        ]);
        var answer = await model.GetResponseAsync([question], new ChatOptions { Temperature = 0, MaxOutputTokens = 600 }, cancellationToken);
        return Read(answer.Text, kinds, width, height);
    }

    private static string Question(IReadOnlyList<ObjectKind> kinds) =>
        $$"""
        Find the objects in this photograph. Look only for these kinds, each given as its code and what it is:
        {{String.Join(Environment.NewLine, kinds.Select(kind => $"- {kind.Code}: {kind.Words}"))}}

        Answer with JSON only: an array with one entry for each kind you can see, at most one entry a kind, each
        {"bbox_2d": [x1, y1, x2, y2], "label": "<code>"}, where the box encloses the object and its coordinates
        are integers from 0 to 1000, in thousandths of the photograph's width (x) and height (y). If you see none
        of them, answer [].
        """;

    /// <summary>
    /// Each entry on its own, so a list the model cut short — on a busy picture it
    /// can repeat one entry until its tokens run out — keeps every whole entry
    /// before the cut. Qwen's grounding answers in the format it was trained on
    /// (<c>bbox_2d</c>, <c>label</c>), which the question now asks for; the plainer
    /// <c>box</c> and <c>kind</c> are read too. The first box of a kind is its box.
    /// </summary>
    private static IReadOnlyList<DetectedObject> Read(string answer, IReadOnlyList<ObjectKind> kinds, int width, int height)
    {
        // Asked in its own format, the model labels a box with the kind's words as
        // often as with its code: either names the kind, in any case and with an
        // underscore for a space.
        var asked = new Dictionary<string, ObjectKind>(StringComparer.OrdinalIgnoreCase);
        foreach (var kind in kinds)
        {
            asked.TryAdd(kind.Code, kind);
            asked.TryAdd(kind.Words, kind);
            asked.TryAdd(kind.Words.Replace(' ', '_'), kind);
        }
        var found = new Dictionary<ObjectKind, DetectedObject>();
        foreach (System.Text.RegularExpressions.Match entry in Entries().Matches(answer))
        {
            // It also copies the question's line whole ("BACKPACK: backpack"): the code is before the colon.
            if (Box(entry.Value) is not ({ } label, { } c) || !asked.TryGetValue(label.Split(':')[0].Trim(), out var kind) || found.ContainsKey(kind))
                continue;

            var (left, top) = ((int)Math.Round(c[0] * width / 1000), (int)Math.Round(c[1] * height / 1000));
            var (right, bottom) = ((int)Math.Round(c[2] * width / 1000), (int)Math.Round(c[3] * height / 1000));
            if (new PictureBox(left, top, right - left, bottom - top).ClippedTo(width, height) is { } inside)
                found[kind] = new DetectedObject(kind, inside, Score: 1);
        }

        return [.. found.Values];
    }

    private static (string? Code, double[]? Corners) Box(string entry)
    {
        try
        {
            using var document = JsonDocument.Parse(entry);
            var root = document.RootElement;
            var code = (root.TryGetProperty("label", out var label) || root.TryGetProperty("kind", out label)) && label.ValueKind is JsonValueKind.String ? label.GetString() : null;
            if ((!root.TryGetProperty("bbox_2d", out var box) && !root.TryGetProperty("box", out box)) || box.ValueKind is not JsonValueKind.Array || box.GetArrayLength() != 4)
                return (code, null);

            var corners = box.EnumerateArray().Select(value => value.TryGetDouble(out var number) ? number : Double.NaN).ToArray();
            return (code, corners.Any(Double.IsNaN) ? null : corners);
        }
        catch (JsonException)
        {
            return (null, null);
        }
    }

    /// <summary>A flat JSON object: an entry holds an array, never another object.</summary>
    [System.Text.RegularExpressions.GeneratedRegex(@"\{[^{}]*\}")]
    private static partial System.Text.RegularExpressions.Regex Entries();

    /// <summary>The picture upright, as a PNG: the server decodes no WebP (I-2), and a phone's orientation is read here, once.</summary>
    private static (int Width, int Height, byte[] Png) Png(ReadOnlyMemory<byte> picture)
    {
        var (rgb, width, height) = SiglipPicture.Upright(picture.Span);
        using var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Opaque));
        var pixels = bitmap.GetPixelSpan();
        for (var at = 0; at < width * height; at++)
        {
            pixels[at * 4] = rgb[at * 3];
            pixels[(at * 4) + 1] = rgb[(at * 3) + 1];
            pixels[(at * 4) + 2] = rgb[(at * 3) + 2];
            pixels[(at * 4) + 3] = 255;
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        return (width, height, encoded.ToArray());
    }
}
