using SkiaSharp;

namespace ElGuerre.Tendero.SeedImages;

/// <summary>
/// A picture file as the square of interleaved RGB bytes the rest of the tool
/// works in. One place, because three callers decoded by hand: the hosted
/// adapter, the probe, and now the checks that compare one picture with another.
/// </summary>
public static class Pixels
{
    /// <summary>
    /// Whatever the bytes are, as a square: the shorter side wins and the
    /// picture is centre-cropped — the object is centred by instruction, so a
    /// crop takes background.
    /// </summary>
    public static (byte[] Rgb, int Size) Square(byte[] encoded)
    {
        using var bitmap = SKBitmap.Decode(encoded)
            ?? throw new InvalidDataException("That is not an image this can read.");

        return Square(bitmap);
    }

    /// <summary>The same picture as a square of <paramref name="side"/> pixels: a hosted model returns the size it feels like.</summary>
    public static byte[] SquareOf(byte[] encoded, int side)
    {
        using var decoded = SKBitmap.Decode(encoded)
            ?? throw new InvalidDataException("That is not an image this can read.");

        if (decoded.Width == side && decoded.Height == side)
            return Square(decoded).Rgb;

        using var scaled = decoded.Resize(new SKImageInfo(side, side), new SKSamplingOptions(SKFilterMode.Linear))
            ?? throw new InvalidDataException("That image could not be scaled.");

        return Square(scaled).Rgb;
    }

    private static (byte[] Rgb, int Size) Square(SKBitmap bitmap)
    {
        var side = Math.Min(bitmap.Width, bitmap.Height);
        var left = (bitmap.Width - side) / 2;
        var top = (bitmap.Height - side) / 2;

        var rgb = new byte[side * side * 3];

        for (var row = 0; row < side; row++)
            for (var column = 0; column < side; column++)
            {
                var pixel = bitmap.GetPixel(left + column, top + row);
                var at = ((row * side) + column) * 3;

                rgb[at] = pixel.Red;
                rgb[at + 1] = pixel.Green;
                rgb[at + 2] = pixel.Blue;
            }

        return (rgb, side);
    }
}
