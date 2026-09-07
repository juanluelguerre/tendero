using SkiaSharp;

namespace ElGuerre.Tendero.SeedImages;

/// <summary>The numbers `seed/IMAGES-TODO.md` measured, in one place.</summary>
public static class ImageSpec
{
    /// <summary>1400 because the product page is the most demanding consumer:
    /// the image column is about 660 CSS pixels in a 1440 frame, which is 1320
    /// real ones on a 2× display. Derivatives are deferred, so this single file
    /// is used everywhere.</summary>
    public const int Size = 1400;

    public const int Quality = 82;

    /// <summary>120 KB. Measured over the first eight: mean 43 KB, worst 84.</summary>
    public const int MaxBytes = 120 * 1024;

    /// <summary>The background, painted rather than left transparent.</summary>
    public static readonly SKColor Background = new(0xFA, 0xF9, 0xF7);

    /// <summary>The product must live inside the central 75% vertically, because
    /// the result card crops to 4:3 with `object-fit: cover` and loses 12.5% off
    /// the top and the bottom.</summary>
    public const float SafeArea = 0.75f;
}

/// <summary>
/// From the decoder's pixels to the file the catalogue references.
///
/// One pass does three jobs, which is cheaper than three and impossible to get
/// half right: the image is drawn onto a surface **pre-filled with the exact
/// background colour**, so there is no alpha channel at any point, the background
/// is the declared grey even if the model drifted a shade, and the halo these
/// generators leave at the edges is covered rather than encoded.
/// </summary>
public static class ImageNormaliser
{
    /// <summary>The quality ladder. The first rung that fits wins, and the tool
    /// says which one it used — an image quietly worse than the other ninety-one
    /// is exactly the drift the register exists to prevent.</summary>
    private static readonly int[] Ladder = [ImageSpec.Quality, 78, 74, 70];

    public static byte[] ToWebp(byte[] rgb, int size, out int quality)
    {
        using var source = FromRgb(rgb, size);

        var info = new SKImageInfo(ImageSpec.Size, ImageSpec.Size, SKColorType.Rgba8888, SKAlphaType.Opaque);
        using var surface = SKSurface.Create(info);

        surface.Canvas.Clear(ImageSpec.Background);

        // Mitchell rather than Catmull-Rom: this register is flat colour with
        // hard edges, and the sharper filter rings on exactly those.
        var sampling = new SKSamplingOptions(new SKCubicResampler(1 / 3f, 1 / 3f));

        using (var image = SKImage.FromBitmap(source))
            surface.Canvas.DrawImage(image, new SKRect(0, 0, ImageSpec.Size, ImageSpec.Size), sampling);

        using var flattened = surface.Snapshot();

        foreach (var rung in Ladder)
        {
            using var encoded = flattened.Encode(SKEncodedImageFormat.Webp, rung);
            var bytes = encoded.ToArray();

            if (bytes.Length <= ImageSpec.MaxBytes || rung == Ladder[^1])
            {
                quality = rung;
                return bytes;
            }
        }

        throw new InvalidOperationException("Unreachable: the ladder always returns on its last rung.");
    }

    private static SKBitmap FromRgb(byte[] rgb, int size)
    {
        var bitmap = new SKBitmap(new SKImageInfo(size, size, SKColorType.Rgba8888, SKAlphaType.Opaque));
        var rgba = new byte[size * size * 4];

        for (var pixel = 0; pixel < size * size; pixel++)
        {
            rgba[(pixel * 4) + 0] = rgb[(pixel * 3) + 0];
            rgba[(pixel * 4) + 1] = rgb[(pixel * 3) + 1];
            rgba[(pixel * 4) + 2] = rgb[(pixel * 3) + 2];
            rgba[(pixel * 4) + 3] = 255;
        }

        bitmap.Pixels = [.. Enumerable.Range(0, size * size).Select(pixel => new SKColor(
            rgba[(pixel * 4) + 0], rgba[(pixel * 4) + 1], rgba[(pixel * 4) + 2], 255))];

        return bitmap;
    }
}

/// <summary>
/// What a WebP file says about itself, read from the container rather than taken
/// on trust from the encoder.
/// </summary>
public static class WebpFacts
{
    /// <summary>
    /// A lossy WebP with no alpha is `RIFF....WEBP` followed by a plain `VP8 `
    /// chunk — note the trailing space. Alpha forces the extended form, `VP8X`
    /// plus an `ALPH` chunk, so three byte comparisons answer exactly the
    /// question the specification asks.
    /// </summary>
    public static bool IsOpaqueLossyWebp(byte[] bytes) =>
        bytes.Length > 16
        && Ascii(bytes, 0) == "RIFF"
        && Ascii(bytes, 8) == "WEBP"
        && Ascii(bytes, 12) == "VP8 ";

    private static string Ascii(byte[] bytes, int at) =>
        System.Text.Encoding.ASCII.GetString(bytes, at, 4);
}

/// <summary>
/// Whether the product stays inside the central band, answered by counting
/// pixels rather than by asking a vision model.
///
/// A language model is poor at spatial extent and would be the largest source of
/// false positives in the whole tool. This is exact, costs a millisecond, and
/// needs no service running.
/// </summary>
public static class SafeAreaProbe
{
    /// <summary>How far from the background a pixel has to be to count as ink.</summary>
    private const int InkThreshold = 12;

    /// <summary>A row needs this share of ink before it counts, so one stray
    /// speckle at the very top does not fail an image.</summary>
    private const float RowFloor = 0.005f;

    public sealed record Result(int FirstRow, int LastRow, int Height, bool Passed, string Detail);

    public static Result Probe(byte[] rgb, int size)
    {
        var minimum = (int)(size * RowFloor);
        int first = -1, last = -1;

        for (var row = 0; row < size; row++)
        {
            var ink = 0;

            for (var column = 0; column < size; column++)
            {
                var at = ((row * size) + column) * 3;

                var distance =
                    Math.Abs(rgb[at] - ImageSpec.Background.Red)
                    + Math.Abs(rgb[at + 1] - ImageSpec.Background.Green)
                    + Math.Abs(rgb[at + 2] - ImageSpec.Background.Blue);

                if (distance > InkThreshold)
                    ink++;
            }

            if (ink <= minimum)
                continue;

            first = first < 0 ? row : first;
            last = row;
        }

        // **An empty image must fail loudly.** A naive minimum and maximum would
        // report a perfectly centred nothing, and a blank frame is exactly what a
        // decoder that overflowed produces.
        if (first < 0)
            return new Result(-1, -1, size, false, "no ink found — the frame is blank");

        var margin = (int)(size * (1f - ImageSpec.SafeArea) / 2f);
        var passed = first >= margin && last <= size - margin;

        var share = (last - first + 1) * 100f / size;

        return new Result(
            first, last, size, passed,
            $"ink spans rows {first}-{last}, {share:F1}% of the height");
    }
}
