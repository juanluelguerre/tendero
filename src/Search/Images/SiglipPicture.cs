using SkiaSharp;

namespace ElGuerre.Tendero.Search.Images;

/// <summary>
/// A picture as SigLIP 2's vision tower reads it (phase 13a, I-5): 224 by 224,
/// three channels, each value from −1 to 1, channel first.
///
/// What the maker's preprocessing does, and nothing else: the picture is resized
/// to the square **without keeping its proportions** and without cropping —
/// stretched, as the model was trained to see it — each byte divided by 255, and
/// then <c>(x − 0.5) / 0.5</c>. A transparent picture is laid over white first:
/// the catalogue's are painted, a shopper's may not be.
///
/// **The resize is written here, not borrowed.** The maker's is Pillow's
/// bilinear, which when it shrinks a picture widens its triangle to the ratio —
/// every source pixel under an output pixel counts — and rounds to a byte after
/// each of its two passes. A graphics library's *linear* is not that: through
/// Skia's, a seed picture's embedding sat at 0.983 of the maker's, enough to
/// reorder neighbours. Forty lines of arithmetic that say what Pillow does are
/// the same pixels on every machine and every version of anything.
/// <c>SiglipOnnxEmbeddingsTests</c> holds the result to the maker's embeddings.
/// </summary>
public static class SiglipPicture
{
    public const int Side = 224;

    /// <summary>The tensor's length: three channels of <see cref="Side"/> by <see cref="Side"/>.</summary>
    public const int Length = 3 * Side * Side;

    /// <summary>The largest picture decoded, a side: a shopper's upload is bounded before it is a bitmap in memory.</summary>
    public const int MaxSide = 8192;

    private const int Channels = 3;

    /// <returns>Channel first — every red, then every green, then every blue — row by row.</returns>
    /// <exception cref="ArgumentException">The bytes are not a picture, or are one too large to take.</exception>
    public static float[] ToTensor(ReadOnlySpan<byte> encoded)
    {
        using var data = SKData.CreateCopy(encoded);
        using var codec = SKCodec.Create(data) ?? throw new ArgumentException("The bytes are not a picture this can decode.", nameof(encoded));
        if (codec.Info.Width > MaxSide || codec.Info.Height > MaxSide)
            throw new ArgumentException($"The picture is {codec.Info.Width} by {codec.Info.Height}: larger than {MaxSide} a side.", nameof(encoded));

        var info = new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using var decoded = new SKBitmap(info);
        if (codec.GetPixels(info, decoded.GetPixels()) is not (SKCodecResult.Success or SKCodecResult.IncompleteInput))
            throw new ArgumentException("The bytes are not a picture this can decode.", nameof(encoded));

        var square = Vertically(Horizontally(OverWhite(decoded.GetPixelSpan(), info.Width, info.Height), info.Width, info.Height), info.Height);

        var tensor = new float[Length];
        const int plane = Side * Side;
        for (var at = 0; at < plane; at++)
        {
            tensor[at] = Scaled(square[at * Channels]);
            tensor[plane + at] = Scaled(square[(at * Channels) + 1]);
            tensor[(2 * plane) + at] = Scaled(square[(at * Channels) + 2]);
        }

        return tensor;
    }

    /// <summary>A byte as the model's value: 0 is −1, 255 is 1.</summary>
    private static float Scaled(byte value) => ((value / 255f) - 0.5f) / 0.5f;

    /// <summary>Red, green and blue of every pixel, each laid over white by its own alpha.</summary>
    private static byte[] OverWhite(ReadOnlySpan<byte> rgba, int width, int height)
    {
        var rgb = new byte[width * height * Channels];
        for (var at = 0; at < width * height; at++)
        {
            var alpha = rgba[(at * 4) + 3];
            for (var channel = 0; channel < Channels; channel++)
            {
                var value = rgba[(at * 4) + channel];
                rgb[(at * Channels) + channel] = alpha == 255 ? value : (byte)(((value * alpha) + (255 * (255 - alpha)) + 127) / 255);
            }
        }

        return rgb;
    }

    /// <summary>
    /// Pillow's bilinear along one axis: for each of the <see cref="Side"/>
    /// outputs, the source pixels its triangle covers and what each weighs. The
    /// triangle is as wide as the ratio when shrinking and one pixel when not.
    /// </summary>
    private static (int First, float[] Weights)[] Taps(int source)
    {
        var scale = (double)source / Side;
        var support = Math.Max(scale, 1.0);
        var taps = new (int, float[])[Side];

        for (var output = 0; output < Side; output++)
        {
            var centre = (output + 0.5) * scale;
            var first = Math.Max((int)(centre - support + 0.5), 0);
            var last = Math.Min((int)(centre + support + 0.5), source);
            var weights = new double[last - first];
            var total = 0.0;
            for (var tap = 0; tap < weights.Length; tap++)
            {
                var distance = Math.Abs((first + tap + 0.5 - centre) / support);
                weights[tap] = distance < 1.0 ? 1.0 - distance : 0.0;
                total += weights[tap];
            }

            taps[output] = (first, [.. weights.Select(weight => (float)(weight / total))]);
        }

        return taps;
    }

    private static byte Rounded(float value) => (byte)Math.Clamp((int)(value + 0.5f), 0, 255);

    /// <summary>Every row from <paramref name="width"/> pixels to <see cref="Side"/>, rounded to bytes as Pillow does between its passes.</summary>
    private static byte[] Horizontally(byte[] rgb, int width, int height)
    {
        var taps = Taps(width);
        var narrowed = new byte[Side * height * Channels];

        for (var row = 0; row < height; row++)
        {
            for (var column = 0; column < Side; column++)
            {
                var (first, weights) = taps[column];
                for (var channel = 0; channel < Channels; channel++)
                {
                    var sum = 0f;
                    for (var tap = 0; tap < weights.Length; tap++)
                        sum += rgb[(((row * width) + first + tap) * Channels) + channel] * weights[tap];
                    narrowed[(((row * Side) + column) * Channels) + channel] = Rounded(sum);
                }
            }
        }

        return narrowed;
    }

    /// <summary>Every column, already <see cref="Side"/> wide, from <paramref name="height"/> pixels to <see cref="Side"/>.</summary>
    private static byte[] Vertically(byte[] narrowed, int height)
    {
        var taps = Taps(height);
        var square = new byte[Side * Side * Channels];

        for (var row = 0; row < Side; row++)
        {
            var (first, weights) = taps[row];
            for (var column = 0; column < Side; column++)
            {
                for (var channel = 0; channel < Channels; channel++)
                {
                    var sum = 0f;
                    for (var tap = 0; tap < weights.Length; tap++)
                        sum += narrowed[((((first + tap) * Side) + column) * Channels) + channel] * weights[tap];
                    square[(((row * Side) + column) * Channels) + channel] = Rounded(sum);
                }
            }
        }

        return square;
    }
}
