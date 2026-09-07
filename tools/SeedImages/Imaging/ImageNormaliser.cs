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

        // **The safe area is applied here, not asked for in the prompt.** Three
        // rewrites of "occupying at most 75% of the frame height, with wide empty
        // margins" produced pictures at 94%; a diffusion model has no notion of
        // the frame it is filling. The object's extent is measured and the
        // drawing is scaled to fit inside the band, which is arithmetic and
        // therefore true every time.
        //
        // The canvas is filled with the background the picture ITSELF uses rather
        // than the declared #FAF9F7, because a scaled drawing on a different grey
        // would leave a visible border where the two meet.
        var bounds = SafeAreaProbe.Measure(rgb, size);

        surface.Canvas.Clear(bounds.Empty ? ImageSpec.Background : bounds.Background);

        // Mitchell rather than Catmull-Rom: this register is flat colour with
        // hard edges, and the sharper filter rings on exactly those.
        var sampling = new SKSamplingOptions(new SKCubicResampler(1 / 3f, 1 / 3f));

        var placement = Placement(bounds, size);

        using (var image = SKImage.FromBitmap(source))
            surface.Canvas.DrawImage(image, placement, sampling);

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

    /// <summary>
    /// Where to draw the whole source so that its INK lands centred inside the
    /// safe band. The scale comes from whichever axis is tighter, and the offset
    /// puts the ink's middle on the frame's middle — so the picture is not
    /// cropped, only placed.
    /// </summary>
    private static SKRect Placement(SafeAreaProbe.Bounds bounds, int size)
    {
        if (bounds.Empty)
            return new SKRect(0, 0, ImageSpec.Size, ImageSpec.Size);

        var band = ImageSpec.Size * ImageSpec.SafeArea;

        var scale = Math.Min(
            Math.Min(band / bounds.Height, band / bounds.Width),
            // Never blow a small object up: a product drawn at a third of the
            // frame is a decision the model made, and enlarging it invents detail.
            ImageSpec.Size / (float)size);

        var width = size * scale;
        var inkCentreX = (bounds.Left + bounds.Right) / 2f * scale;
        var inkCentreY = (bounds.Top + bounds.Bottom) / 2f * scale;

        var left = (ImageSpec.Size / 2f) - inkCentreX;
        var top = (ImageSpec.Size / 2f) - inkCentreY;

        return new SKRect(left, top, left + width, top + width);
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

    /// <summary>Where the ink is, in both axes, and what the background is.</summary>
    public sealed record Bounds(int Top, int Bottom, int Left, int Right, SKColor Background)
    {
        public int Height => Bottom - Top + 1;

        public int Width => Right - Left + 1;

        public bool Empty => Bottom < Top;
    }

    /// <summary>
    /// The object's extent, used by the normaliser to place it rather than only
    /// to judge it. Asking a diffusion model to leave a margin is asking it for
    /// the one thing it is worst at; measuring the margin and applying it is
    /// arithmetic.
    /// </summary>
    public static Bounds Measure(byte[] rgb, int size)
    {
        var background = BorderColour(rgb, size);
        var minimum = (int)(size * RowFloor);

        int top = size, bottom = -1, left = size, right = -1;

        for (var row = 0; row < size; row++)
        {
            var ink = 0;
            int rowLeft = size, rowRight = -1;

            for (var column = 0; column < size; column++)
            {
                var at = ((row * size) + column) * 3;

                var distance =
                    Math.Abs(rgb[at] - background.Red)
                    + Math.Abs(rgb[at + 1] - background.Green)
                    + Math.Abs(rgb[at + 2] - background.Blue);

                if (distance <= InkThreshold)
                    continue;

                ink++;
                rowLeft = Math.Min(rowLeft, column);
                rowRight = Math.Max(rowRight, column);
            }

            if (ink <= minimum)
                continue;

            top = Math.Min(top, row);
            bottom = row;
            left = Math.Min(left, rowLeft);
            right = Math.Max(right, rowRight);
        }

        return new Bounds(top, bottom, left, right,
            new SKColor(background.Red, background.Green, background.Blue));
    }

    public static Result Probe(byte[] rgb, int size)
    {
        // **The background is whatever the model painted, not the colour the
        // specification asks for.** The first version measured distance from
        // #FAF9F7 and reported that a perfectly framed backpack filled the entire
        // frame -- because the model had produced a plain grey that was simply a
        // different plain grey. Reading it off the picture makes the probe about
        // the object's extent, which is the question, instead of about the
        // palette, which is a separate one.
        var background = BorderColour(rgb, size);

        var minimum = (int)(size * RowFloor);
        int first = -1, last = -1;

        for (var row = 0; row < size; row++)
        {
            var ink = 0;

            for (var column = 0; column < size; column++)
            {
                var at = ((row * size) + column) * 3;

                var distance =
                    Math.Abs(rgb[at] - background.Red)
                    + Math.Abs(rgb[at + 1] - background.Green)
                    + Math.Abs(rgb[at + 2] - background.Blue);

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

    /// <summary>
    /// The most common colour around the one-pixel border.
    ///
    /// Corners alone are not enough — an object that reaches the top of the frame
    /// puts itself in two of them, and the probe then measures the picture
    /// against the product and reports a blank. The whole ring is overwhelmingly
    /// background unless the object bleeds off all four sides, and an image like
    /// that fails for other reasons anyway.
    ///
    /// Colours are bucketed to five bits a channel first, because a generated
    /// background is flat to the eye and not to a byte comparison.
    /// </summary>
    private static (byte Red, byte Green, byte Blue) BorderColour(byte[] rgb, int size)
    {
        var counts = new Dictionary<int, (int Count, byte Red, byte Green, byte Blue)>();

        void Sample(int row, int column)
        {
            var at = ((row * size) + column) * 3;
            byte red = rgb[at], green = rgb[at + 1], blue = rgb[at + 2];

            var bucket = ((red >> 3) << 10) | ((green >> 3) << 5) | (blue >> 3);
            var seen = counts.GetValueOrDefault(bucket);

            counts[bucket] = (seen.Count + 1, red, green, blue);
        }

        for (var column = 0; column < size; column++)
        {
            Sample(0, column);
            Sample(size - 1, column);
        }

        for (var row = 1; row < size - 1; row++)
        {
            Sample(row, 0);
            Sample(row, size - 1);
        }

        var winner = counts.MaxBy(entry => entry.Value.Count).Value;

        return (winner.Red, winner.Green, winner.Blue);
    }
}
