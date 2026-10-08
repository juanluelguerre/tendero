using ElGuerre.Tendero.Search.Images;
using SkiaSharp;
using Xunit;

namespace ElGuerre.Tendero.Search.Tests.Images;

/// <summary>
/// A picture as SigLIP 2's vision tower reads it (phase 13a, I-5), with no model
/// at all: the square, the three planes in their order, the range, and what is
/// refused. These run everywhere; whether the result embeds as the maker's does
/// is <see cref="SiglipOnnxEmbeddingsTests"/>'s, where the model is.
/// </summary>
public sealed class SiglipPictureTests
{
    private const int Plane = SiglipPicture.Side * SiglipPicture.Side;

    private static byte[] Png(int width, int height, Action<SKCanvas> draw, SKAlphaType alpha = SKAlphaType.Opaque)
    {
        using var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, alpha));
        using (var canvas = new SKCanvas(bitmap))
            draw(canvas);
        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        return encoded.ToArray();
    }

    [Theory]
    [InlineData(255, 255, 255, 1f, 1f, 1f)]
    [InlineData(0, 0, 0, -1f, -1f, -1f)]
    // Channel first, red then green then blue: a picture of one colour is three flat planes.
    [InlineData(255, 0, 0, 1f, -1f, -1f)]
    [InlineData(0, 255, 0, -1f, 1f, -1f)]
    [InlineData(0, 0, 255, -1f, -1f, 1f)]
    public void A_picture_of_one_colour_is_three_flat_planes_from_minus_one_to_one(byte r, byte g, byte b, float red, float green, float blue)
    {
        var tensor = SiglipPicture.ToTensor(Png(40, 40, canvas => canvas.Clear(new SKColor(r, g, b))));

        Assert.Equal(SiglipPicture.Length, tensor.Length);
        Assert.All(tensor.AsSpan(0, Plane).ToArray(), value => Assert.Equal(red, value, 3));
        Assert.All(tensor.AsSpan(Plane, Plane).ToArray(), value => Assert.Equal(green, value, 3));
        Assert.All(tensor.AsSpan(2 * Plane, Plane).ToArray(), value => Assert.Equal(blue, value, 3));
    }

    /// <summary>A mid grey is zero, near enough: the scale is centred, not from nought to one.</summary>
    [Fact]
    public void Mid_grey_is_near_zero()
    {
        var tensor = SiglipPicture.ToTensor(Png(10, 10, canvas => canvas.Clear(new SKColor(128, 128, 128))));

        Assert.All(tensor, value => Assert.InRange(value, -0.01f, 0.01f));
    }

    /// <summary>
    /// **Stretched, not cropped**, as the model was trained to see: a picture
    /// twice as wide as tall, black on its left half and white on its right,
    /// is still half and half once square — row by row, left then right.
    /// </summary>
    [Fact]
    public void A_picture_that_is_not_square_is_stretched_to_one_and_not_cropped()
    {
        var wide = Png(400, 200, canvas =>
        {
            canvas.Clear(SKColors.White);
            using var black = new SKPaint { Color = SKColors.Black };
            canvas.DrawRect(0, 0, 200, 200, black);
        });

        var tensor = SiglipPicture.ToTensor(wide);

        float At(int row, int column) => tensor[(row * SiglipPicture.Side) + column];
        Assert.Equal((-1f, -1f, 1f, 1f), (At(5, 10), At(200, 100), At(5, 125), At(200, 215)));
    }

    /// <summary>A shopper's picture may be transparent where the catalogue's are painted: it is laid over white, not over black.</summary>
    [Fact]
    public void A_transparent_picture_is_laid_over_white()
    {
        var tensor = SiglipPicture.ToTensor(Png(30, 30, canvas => canvas.Clear(SKColors.Transparent), SKAlphaType.Premul));

        Assert.All(tensor, value => Assert.Equal(1f, value, 3));
    }

    /// <summary>
    /// A phone stores a portrait as a landscape and a note that says "turn me".
    /// Read without the note, a shopper's photograph of a lamp is a lamp lying on
    /// its side — and its neighbours are whatever lies on its side.
    ///
    /// Four quadrants — red, green, blue, white — stored once and labelled with
    /// each of EXIF's eight orientations; what the model is given is the picture
    /// as a person sees it. Top left, top right, bottom left, bottom right.
    /// </summary>
    [Theory]
    [InlineData(1, "RGBW")] // as stored
    [InlineData(2, "GRWB")] // mirrored left to right
    [InlineData(3, "WBGR")] // upside down
    [InlineData(4, "BWRG")] // mirrored top to bottom
    [InlineData(5, "RBGW")] // transposed
    [InlineData(6, "BRWG")] // a quarter turn clockwise: the phone held upright
    [InlineData(7, "WGBR")] // transverse
    [InlineData(8, "GWRB")] // a quarter turn the other way
    public void A_photograph_is_read_the_way_up_its_orientation_says(int orientation, string seen)
    {
        var tensor = SiglipPicture.ToTensor(QuadrantsSaying(orientation));

        char At(int row, int column)
        {
            var at = (row * SiglipPicture.Side) + column;
            return (tensor[at] > 0, tensor[Plane + at] > 0, tensor[(2 * Plane) + at] > 0) switch
            {
                (true, false, false) => 'R',
                (false, true, false) => 'G',
                (false, false, true) => 'B',
                (true, true, true) => 'W',
                _ => '?',
            };
        }

        Assert.Equal(seen, string.Concat(At(56, 56), At(56, 168), At(168, 56), At(168, 168)));
    }

    /// <summary>
    /// A JPEG of four quadrants, wider than tall, with an EXIF block that says
    /// only how it is to be turned — written by hand, because nothing here writes
    /// EXIF and thirty-four bytes are easier to read than a dependency.
    /// </summary>
    private static byte[] QuadrantsSaying(int orientation)
    {
        using var bitmap = new SKBitmap(new SKImageInfo(200, 100, SKColorType.Rgba8888, SKAlphaType.Opaque));
        using (var canvas = new SKCanvas(bitmap))
        {
            void Quadrant(int x, int y, SKColor colour)
            {
                using var paint = new SKPaint { Color = colour };
                canvas.DrawRect(x, y, 100, 50, paint);
            }

            Quadrant(0, 0, new SKColor(255, 0, 0));
            Quadrant(100, 0, new SKColor(0, 255, 0));
            Quadrant(0, 50, new SKColor(0, 0, 255));
            Quadrant(100, 50, new SKColor(255, 255, 255));
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Jpeg, 100);
        var jpeg = encoded.ToArray();

        byte[] exif =
        [
            0xFF, 0xE1, 0x00, 0x22,                         // APP1, thirty-four bytes with this length
            (byte)'E', (byte)'x', (byte)'i', (byte)'f', 0, 0,
            (byte)'I', (byte)'I', 0x2A, 0x00, 0x08, 0, 0, 0, // TIFF, little endian, its directory eight bytes in
            0x01, 0x00,                                     // one entry
            0x12, 0x01, 0x03, 0x00, 0x01, 0, 0, 0, (byte)orientation, 0, 0, 0, // Orientation, a short, one of them
            0, 0, 0, 0,                                     // and no directory after it
        ];

        // After the two bytes that say "JPEG", before everything else.
        return [.. jpeg[..2], .. exif, .. jpeg[2..]];
    }

    [Fact]
    public void Bytes_that_are_not_a_picture_are_refused()
    {
        Assert.Throws<ArgumentException>(() => SiglipPicture.ToTensor("not a picture"u8));
        Assert.Throws<ArgumentException>(() => SiglipPicture.ToTensor([]));
    }

    /// <summary>A picture larger than the limit is refused before it is a bitmap in memory: what a shopper uploads is bounded.</summary>
    [Fact]
    public void A_picture_wider_than_the_limit_is_refused_and_one_at_the_limit_is_not()
    {
        var refusal = Assert.Throws<ArgumentException>(() => SiglipPicture.ToTensor(Png(SiglipPicture.MaxSide + 1, 1, canvas => canvas.Clear(SKColors.White))));

        Assert.Contains("larger than", refusal.Message, StringComparison.Ordinal);
        Assert.Equal(SiglipPicture.Length, SiglipPicture.ToTensor(Png(SiglipPicture.MaxSide, 1, canvas => canvas.Clear(SKColors.White))).Length);
    }
}
