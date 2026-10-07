using ElGuerre.Tendero.SeedImages;
using SkiaSharp;
using Xunit;

namespace ElGuerre.Tendero.SeedImages.Tests;

/// <summary>
/// A known-bad picture drawn on purpose (the phase 13a spec, I-2b): a defect of
/// one kind put into a picture that had none, by code, so what a question is
/// measured against is known by construction and costs no call to a generator.
/// </summary>
public sealed class DefectForgeTests
{
    private const int Size = 200;

    /// <summary>A picture as the catalogue's are: the declared background, and one coloured object in the middle.</summary>
    private static byte[] Picture(SKColor? colour = null)
    {
        using var bitmap = new SKBitmap(Size, Size);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(ImageSpec.Background);
            using var paint = new SKPaint { Color = colour ?? new SKColor(0x2E, 0x6B, 0x3A), IsAntialias = false };
            canvas.DrawRect(SKRect.Create(60, 60, 80, 80), paint);
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        return encoded.ToArray();
    }

    private static SKBitmap Decode(byte[] bytes) => SKBitmap.Decode(bytes);

    private static bool IsBackground(SKColor pixel) =>
        Math.Abs(pixel.Red - ImageSpec.Background.Red) + Math.Abs(pixel.Green - ImageSpec.Background.Green) + Math.Abs(pixel.Blue - ImageSpec.Background.Blue) < 12;

    public static TheoryData<Defect> Kinds => [.. Enum.GetValues<Defect>().Where(defect => defect != Defect.Form)];

    /// <summary>
    /// A knife standing blade up in its block is the product drawn wrong, and no
    /// code can draw that into a right picture: the kind exists for the pictures a
    /// person rejects, and the forge says it has nothing to give.
    /// </summary>
    [Fact]
    public void A_defect_of_form_is_only_ever_a_rejected_picture()
    {
        Assert.Null(DefectForge.Draw(Defect.Form, Picture()));
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public void Every_kind_of_defect_gives_a_picture_of_the_same_size_that_is_not_the_one_it_was_given(Defect defect)
    {
        var good = Picture();

        var bad = DefectForge.Draw(defect, good);

        Assert.NotNull(bad);
        using var before = Decode(good);
        using var after = Decode(bad);
        Assert.Equal((before.Width, before.Height), (after.Width, after.Height));
        Assert.NotEqual(before.Bytes, after.Bytes);
    }

    /// <summary>A measurement that changes between runs is not one: the same picture and kind give the same bytes.</summary>
    [Theory]
    [MemberData(nameof(Kinds))]
    public void The_same_picture_and_kind_give_the_same_bytes(Defect defect)
    {
        var good = Picture();

        Assert.Equal(DefectForge.Draw(defect, good), DefectForge.Draw(defect, good));
    }

    /// <summary>Two where there was one: the object is drawn in each half and the middle, where it was, is left empty.</summary>
    [Fact]
    public void A_count_defect_draws_the_object_twice()
    {
        using var bad = Decode(DefectForge.Draw(Defect.Count, Picture())!);

        Assert.False(IsBackground(bad.GetPixel(Size / 4, Size / 2)));
        Assert.False(IsBackground(bad.GetPixel(3 * Size / 4, Size / 2)));
        Assert.True(IsBackground(bad.GetPixel(Size / 2, Size / 2)));
    }

    /// <summary>The emblem and the lettering are drawn on the object, where a generator puts them — not in the margin.</summary>
    [Theory]
    [InlineData(Defect.Emblem)]
    [InlineData(Defect.Lettering)]
    public void A_mark_is_drawn_inside_the_object_and_nowhere_else(Defect defect)
    {
        var good = Picture();
        using var before = Decode(good);
        using var after = Decode(DefectForge.Draw(defect, good)!);

        var changed = 0;
        for (var y = 0; y < Size; y++)
        {
            for (var x = 0; x < Size; x++)
            {
                if (before.GetPixel(x, y) == after.GetPixel(x, y))
                    continue;

                changed++;
                Assert.InRange(x, 60, 139);
                Assert.InRange(y, 60, 139);
            }
        }

        Assert.True(changed > 0);
    }

    /// <summary>The background stops being the catalogue's flat one, and the object is left as it was.</summary>
    [Fact]
    public void A_background_defect_repaints_what_was_background_and_only_that()
    {
        var good = Picture();
        using var before = Decode(good);
        using var after = Decode(DefectForge.Draw(Defect.Background, good)!);

        Assert.False(IsBackground(after.GetPixel(10, 190)));
        Assert.Equal(before.GetPixel(100, 100), after.GetPixel(100, 100));
    }

    /// <summary>
    /// A slipper's white collar and a lamp's neck are the background's colour and
    /// are not background: only what reaches the picture's edge is repainted. The
    /// first set drew a blue collar on the slipper, which a person saw on the sheet.
    /// </summary>
    [Fact]
    public void A_part_of_the_product_in_the_backgrounds_colour_is_not_repainted()
    {
        using var bitmap = new SKBitmap(Size, Size);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(ImageSpec.Background);
            using var body = new SKPaint { Color = new SKColor(0x2E, 0x6B, 0x3A), IsAntialias = false };
            using var collar = new SKPaint { Color = ImageSpec.Background, IsAntialias = false };
            canvas.DrawRect(SKRect.Create(60, 60, 80, 80), body);
            canvas.DrawRect(SKRect.Create(90, 90, 20, 20), collar);
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);

        using var after = Decode(DefectForge.Draw(Defect.Background, encoded.ToArray())!);

        Assert.True(IsBackground(after.GetPixel(100, 100)));
        Assert.False(IsBackground(after.GetPixel(10, 10)));
    }

    /// <summary>The object changes colour and the background does not.</summary>
    [Fact]
    public void A_colour_defect_turns_the_objects_hue_and_leaves_the_background()
    {
        var good = Picture();
        using var before = Decode(good);
        using var after = Decode(DefectForge.Draw(Defect.Colour, good)!);

        Assert.NotEqual(before.GetPixel(100, 100), after.GetPixel(100, 100));
        Assert.True(IsBackground(after.GetPixel(10, 10)));
    }

    /// <summary>A grey object has no hue to turn: there is no colour defect to draw, and the forge says so instead of handing back the same picture as a bad one.</summary>
    [Fact]
    public void A_grey_object_has_no_colour_defect()
    {
        Assert.Null(DefectForge.Draw(Defect.Colour, Picture(new SKColor(0x80, 0x80, 0x80))));
    }

    /// <summary>Something is drawn besides the product, outside where the product is.</summary>
    [Fact]
    public void A_scene_defect_draws_something_beside_the_object()
    {
        var good = Picture();
        using var before = Decode(good);
        using var after = Decode(DefectForge.Draw(Defect.Scene, good)!);

        Assert.Equal(before.GetPixel(100, 100), after.GetPixel(100, 100));
        Assert.False(IsBackground(after.GetPixel(Size / 2, Size - 4)));
    }
}
