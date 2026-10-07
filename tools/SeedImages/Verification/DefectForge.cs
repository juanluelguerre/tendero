using SkiaSharp;

namespace ElGuerre.Tendero.SeedImages;

/// <summary>The kinds of thing a generated picture gets wrong while still being the product.</summary>
public enum Defect
{
    /// <summary>Letters or a figure on the product: `4 mm` on a midsole, `24h` on a display.</summary>
    Lettering,

    /// <summary>A symbol on the product: the hand-wash emblem on the first batch's slipper.</summary>
    Emblem,

    /// <summary>Something drawn besides the product: a surface, a second object.</summary>
    Scene,

    /// <summary>The product in a colour the catalogue does not declare for it.</summary>
    Colour,

    /// <summary>More of the product than it is: two where there is one.</summary>
    Count,

    /// <summary>A background that is not the catalogue's flat one.</summary>
    Background,

    /// <summary>
    /// The product itself drawn wrong: a knife standing blade up in its block.
    /// **Only ever a rejected picture** — no code draws this into a right one.
    /// </summary>
    Form,
}

/// <summary>
/// A known-bad picture, drawn on purpose (phase 13a, I-2b).
///
/// The questions were measured on pictures shown with another product's data —
/// a wrong thing — and what a generator gets wrong is the right thing drawn
/// wrong. The rejected pictures that would measure that were deleted when they
/// were redrawn, and drawing them again is a paid call each. So the defect is put
/// into a picture that has none, by code: its kind is known by construction, it
/// is the same on every run, and it costs nothing.
///
/// **What this is not**: a generator's defect. An emblem stamped here is cleaner
/// and more central than one a model draws, so a question that catches these has
/// passed the easy case. The pictures a person really rejects are kept beside
/// these from now on (`reject`), and they are the harder half of the set.
/// </summary>
public static class DefectForge
{
    /// <summary>How far a pixel may be from the declared background and still be it: WebP's noise is a few levels.</summary>
    private const int BackgroundTolerance = 24;

    /// <summary>Below this a pixel is grey for the purpose of turning its hue.</summary>
    private const float Chromatic = 0.25f;

    private const float HueTurn = 120f;

    private static readonly SKColor Ink = new(0x22, 0x22, 0x22);

    /// <summary>The picture with the defect drawn into it, as PNG — or null when this picture cannot carry that defect.</summary>
    public static byte[]? Draw(Defect defect, byte[] picture)
    {
        using var good = Decode(picture);

        using var bad = defect switch
        {
            Defect.Lettering => Lettered(good),
            Defect.Emblem => Stamped(good),
            Defect.Scene => OnASurface(good),
            Defect.Colour => Recoloured(good),
            Defect.Count => Twice(good),
            Defect.Background => Graded(good),
            Defect.Form => null,
            _ => throw new ArgumentOutOfRangeException(nameof(defect), defect, null),
        };

        if (bad is null)
            return null;

        using var image = SKImage.FromBitmap(bad);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        return encoded.ToArray();
    }

    private static SKBitmap Decode(byte[] picture)
    {
        using var data = SKData.CreateCopy(picture);
        using var codec = SKCodec.Create(data)
            ?? throw new InvalidDataException("That is not an image this can decode.");

        return SKBitmap.Decode(codec)
            ?? throw new InvalidDataException("That is not an image this can decode.");
    }

    private static bool IsBackground(SKColor pixel) =>
        Math.Abs(pixel.Red - ImageSpec.Background.Red)
        + Math.Abs(pixel.Green - ImageSpec.Background.Green)
        + Math.Abs(pixel.Blue - ImageSpec.Background.Blue) < BackgroundTolerance;

    /// <summary>The box round everything that is not background, or the whole picture when nothing is.</summary>
    private static SKRectI Bounds(SKBitmap bitmap)
    {
        var (left, top, right, bottom) = (bitmap.Width, bitmap.Height, -1, -1);

        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                if (IsBackground(bitmap.GetPixel(x, y)))
                    continue;

                (left, top, right, bottom) = (Math.Min(left, x), Math.Min(top, y), Math.Max(right, x), Math.Max(bottom, y));
            }
        }

        return right < 0 ? new SKRectI(0, 0, bitmap.Width, bitmap.Height) : new SKRectI(left, top, right + 1, bottom + 1);
    }

    /// <summary>
    /// Where on the product a mark of this size goes: the place nearest the middle
    /// of its box that is product all over. A sandal, a lamp or a pair of shoes is
    /// mostly not there in the middle of its own box, and a mark drawn on the
    /// background is a different defect.
    /// </summary>
    private static SKPointI Anchor(SKBitmap bitmap, SKRectI bounds, int halfWidth, int halfHeight)
    {
        var centre = new SKPointI(bounds.MidX, bounds.MidY);
        var step = Math.Max(1, Math.Min(bounds.Width, bounds.Height) / 24);
        var (best, bestCover, bestDistance) = (centre, -1, long.MaxValue);

        for (var y = bounds.Top + halfHeight; y <= bounds.Bottom - halfHeight; y += step)
        {
            for (var x = bounds.Left + halfWidth; x <= bounds.Right - halfWidth; x += step)
            {
                var cover = 0;
                for (var row = -2; row <= 2; row++)
                {
                    for (var column = -2; column <= 2; column++)
                    {
                        var (sampleX, sampleY) = (x + (column * halfWidth / 2), y + (row * halfHeight / 2));
                        if (sampleX >= 0 && sampleY >= 0 && sampleX < bitmap.Width && sampleY < bitmap.Height
                            && !IsBackground(bitmap.GetPixel(sampleX, sampleY)))
                        {
                            cover++;
                        }
                    }
                }

                long distance = ((x - centre.X) * (x - centre.X)) + ((y - centre.Y) * (y - centre.Y));
                if (cover > bestCover || (cover == bestCover && distance < bestDistance))
                    (best, bestCover, bestDistance) = (new SKPointI(x, y), cover, distance);
            }
        }

        return best;
    }

    /// <summary>Dark on a light product and light on a dark one: a mark nobody could see measures nothing.</summary>
    private static SKColor Legible(SKBitmap bitmap, SKPointI at)
    {
        var under = bitmap.GetPixel(Math.Clamp(at.X, 0, bitmap.Width - 1), Math.Clamp(at.Y, 0, bitmap.Height - 1));
        var luminance = ((0.299 * under.Red) + (0.587 * under.Green) + (0.114 * under.Blue)) / 255;
        return luminance > 0.5 ? Ink : SKColors.White;
    }

    /// <summary>
    /// `4 mm`, in strokes and not in a typeface: a font is whatever the machine has
    /// installed, and a container with none draws nothing and says nothing.
    /// </summary>
    private static SKBitmap Lettered(SKBitmap good)
    {
        var bad = good.Copy();
        var bounds = Bounds(good);
        var unit = Math.Max(1, (int)MathF.Round(Math.Min(bounds.Width, bounds.Height) * 0.035f));

        // On a grid of units: "4", a space, "m", "m" — 10.5 wide and 4 tall.
        var (width, height) = ((int)(10.5f * unit), 4 * unit);
        var at = Anchor(good, bounds, (width / 2) + unit, (height / 2) + unit);
        var (left, top) = (at.X - (width / 2), at.Y - (height / 2));

        using var canvas = new SKCanvas(bad);
        using var paint = new SKPaint
        {
            Color = Legible(good, at),
            IsAntialias = false,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = Math.Max(1, unit / 2),
            StrokeCap = SKStrokeCap.Square,
        };

        void Stroke(float fromX, float fromY, float toX, float toY) =>
            canvas.DrawLine(left + (fromX * unit), top + (fromY * unit), left + (toX * unit), top + (toY * unit), paint);

        Stroke(0, 0, 0, 2);
        Stroke(0, 2, 2, 2);
        Stroke(2, 0, 2, 4);

        foreach (var letter in (float[])[3.5f, 7.5f])
        {
            Stroke(letter, 1.5f, letter, 4);
            Stroke(letter + 1.5f, 1.5f, letter + 1.5f, 4);
            Stroke(letter + 3, 1.5f, letter + 3, 4);
            Stroke(letter, 1.5f, letter + 3, 1.5f);
        }

        return bad;
    }

    /// <summary>A round emblem with a wash tub in it, as the slipper's was.</summary>
    private static SKBitmap Stamped(SKBitmap good)
    {
        var bad = good.Copy();
        var bounds = Bounds(good);
        var radius = Math.Max(3, (int)MathF.Round(Math.Min(bounds.Width, bounds.Height) * 0.07f));
        var at = Anchor(good, bounds, radius + 1, radius + 1);

        using var canvas = new SKCanvas(bad);
        using var fill = new SKPaint { Color = SKColors.White, IsAntialias = false, Style = SKPaintStyle.Fill };
        using var line = new SKPaint
        {
            Color = Ink,
            IsAntialias = false,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = Math.Max(1, radius / 6),
        };

        canvas.DrawCircle(at.X, at.Y, radius, fill);
        canvas.DrawCircle(at.X, at.Y, radius, line);

        // The tub: its two sides, its bottom, and the water's line across it.
        var (half, depth) = (radius * 0.5f, radius * 0.45f);
        canvas.DrawLine(at.X - half, at.Y - (depth / 2), at.X - (half * 0.7f), at.Y + depth, line);
        canvas.DrawLine(at.X + half, at.Y - (depth / 2), at.X + (half * 0.7f), at.Y + depth, line);
        canvas.DrawLine(at.X - (half * 0.7f), at.Y + depth, at.X + (half * 0.7f), at.Y + depth, line);
        canvas.DrawLine(at.X - half, at.Y, at.X + half, at.Y, line);

        return bad;
    }

    /// <summary>A surface under the picture and a ball standing on it: something drawn besides the product.</summary>
    private static SKBitmap OnASurface(SKBitmap good)
    {
        var bad = good.Copy();
        var (width, height) = (good.Width, good.Height);

        using var canvas = new SKCanvas(bad);
        using var wood = new SKPaint { Color = new SKColor(0x8A, 0x5A, 0x33), IsAntialias = false };
        using var ball = new SKPaint { Color = new SKColor(0xC2, 0x3B, 0x2E), IsAntialias = false };

        // Below the central 75% the product is held to, so it is beside it and not over it.
        var surface = (int)(height * 0.89f);
        canvas.DrawRect(SKRect.Create(0, surface, width, height - surface), wood);
        canvas.DrawCircle(width * 0.92f, surface - (width * 0.035f), width * 0.035f, ball);

        return bad;
    }

    /// <summary>
    /// The product's hue turned a third of the way round, the background left —
    /// or nothing, for a product with no hue to turn: a grey shoe turned is the
    /// same grey shoe, and handing it back as a bad picture would count a miss
    /// that is the forge's.
    /// </summary>
    private static SKBitmap? Recoloured(SKBitmap good)
    {
        var bad = good.Copy();
        var (product, turned) = (0, 0);

        for (var y = 0; y < good.Height; y++)
        {
            for (var x = 0; x < good.Width; x++)
            {
                var pixel = good.GetPixel(x, y);
                if (IsBackground(pixel))
                    continue;

                product++;
                pixel.ToHsv(out var hue, out var saturation, out var value);
                if (saturation / 100f < Chromatic)
                    continue;

                turned++;
                bad.SetPixel(x, y, SKColor.FromHsv((hue + HueTurn) % 360f, saturation, value));
            }
        }

        if (product > 0 && turned * 5 >= product)
            return bad;

        bad.Dispose();
        return null;
    }

    /// <summary>The product at half its size, twice, side by side on the catalogue's background.</summary>
    private static SKBitmap Twice(SKBitmap good)
    {
        var bad = new SKBitmap(good.Info);
        var (width, height) = (good.Width, good.Height);

        using var canvas = new SKCanvas(bad);
        using var image = SKImage.FromBitmap(good);
        canvas.Clear(ImageSpec.Background);

        var sampling = new SKSamplingOptions(SKFilterMode.Nearest);
        canvas.DrawImage(image, SKRect.Create(0, height / 4f, width / 2f, height / 2f), sampling);
        canvas.DrawImage(image, SKRect.Create(width / 2f, height / 4f, width / 2f, height / 2f), sampling);

        return bad;
    }

    /// <summary>
    /// What was background becomes a blue-grey that darkens downwards: a studio
    /// backdrop, not the catalogue's flat one. **Background is what reaches the
    /// picture's edge**, not whatever has its colour: a slipper's white collar is
    /// the same near-white and is the product.
    /// </summary>
    private static SKBitmap Graded(SKBitmap good)
    {
        var bad = good.Copy();
        var (width, height) = (good.Width, good.Height);
        var reached = new bool[width * height];
        Queue<(int X, int Y)> edge = [];

        void Reach(int x, int y)
        {
            if (x < 0 || y < 0 || x >= width || y >= height || reached[(y * width) + x] || !IsBackground(good.GetPixel(x, y)))
                return;

            reached[(y * width) + x] = true;
            edge.Enqueue((x, y));
        }

        for (var x = 0; x < width; x++)
        {
            Reach(x, 0);
            Reach(x, height - 1);
        }

        for (var y = 0; y < height; y++)
        {
            Reach(0, y);
            Reach(width - 1, y);
        }

        while (edge.TryDequeue(out var at))
        {
            var down = height == 1 ? 0f : at.Y / (float)(height - 1);
            bad.SetPixel(at.X, at.Y, new SKColor(
                (byte)(0xB8 - (down * (0xB8 - 0x6E))),
                (byte)(0xC4 - (down * (0xC4 - 0x7F))),
                (byte)(0xD6 - (down * (0xD6 - 0x9A)))));

            Reach(at.X + 1, at.Y);
            Reach(at.X - 1, at.Y);
            Reach(at.X, at.Y + 1);
            Reach(at.X, at.Y - 1);
        }

        return bad;
    }
}
