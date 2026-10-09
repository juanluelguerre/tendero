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

    private static readonly SKColor Navy = new(0x23, 0x3A, 0x5E);
    private static readonly SKColor Terracotta = new(0xC0, 0x64, 0x3F);

    /// <summary>Colours as the catalogue declares them: three with a hue, and two a hue cannot be turned to.</summary>
    private static readonly string[] Swatches = ["#233A5E", "#FFFFFF", "#2F5D3A", "#8A847C", "#C0643F"];

    /// <summary>A picture as the catalogue's are: the declared background, and one coloured object in the middle.</summary>
    private static byte[] Picture(SKColor? colour = null, Action<SKCanvas>? with = null)
    {
        using var bitmap = new SKBitmap(Size, Size);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(ImageSpec.Background);
            using var paint = new SKPaint { Color = colour ?? new SKColor(0x2E, 0x6B, 0x3A), IsAntialias = false };
            canvas.DrawRect(SKRect.Create(60, 60, 80, 80), paint);
            with?.Invoke(canvas);
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
        Assert.Null(DefectForge.Draw(Defect.Form, Picture(), Swatches));
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public void Every_kind_of_defect_gives_a_picture_of_the_same_size_that_is_not_the_one_it_was_given(Defect defect)
    {
        var good = Picture();

        var bad = DefectForge.Draw(defect, good, Swatches);

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

        Assert.Equal(DefectForge.Draw(defect, good, Swatches), DefectForge.Draw(defect, good, Swatches));
    }

    /// <summary>Two where there was one: the object is drawn in each half and the middle, where it was, is left empty.</summary>
    [Fact]
    public void A_count_defect_draws_the_object_twice()
    {
        using var bad = Decode(DefectForge.Draw(Defect.Count, Picture(), Swatches)!);

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
        using var after = Decode(DefectForge.Draw(defect, good, Swatches)!);

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
        using var after = Decode(DefectForge.Draw(Defect.Background, good, Swatches)!);

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

        using var after = Decode(DefectForge.Draw(Defect.Background, encoded.ToArray(), Swatches)!);

        Assert.True(IsBackground(after.GetPixel(100, 100)));
        Assert.False(IsBackground(after.GetPixel(10, 10)));
    }

    private static void AssertNear(SKColor expected, SKColor actual)
    {
        var apart = Math.Abs(expected.Red - actual.Red) + Math.Abs(expected.Green - actual.Green) + Math.Abs(expected.Blue - actual.Blue);
        Assert.True(apart <= 6, $"expected about {expected}, got {actual}");
    }

    /// <summary>
    /// The object comes out **another of the catalogue's colours**, the one
    /// furthest round from its own, and the background is left. Turned by a
    /// fixed third, a green gilet came out a purple the catalogue has no name
    /// for, and a question that answers with the catalogue's names had nothing
    /// to say about it.
    /// </summary>
    [Fact]
    public void A_colour_defect_is_the_object_in_another_of_the_catalogues_colours()
    {
        var good = Picture();
        using var after = Decode(DefectForge.Draw(Defect.Colour, good, Swatches)!);

        AssertNear(Terracotta, after.GetPixel(100, 100));
        Assert.True(IsBackground(after.GetPixel(10, 10)));
    }

    /// <summary>Which colour depends on the object's own: a terracotta one is furthest from navy.</summary>
    [Fact]
    public void The_colour_it_becomes_is_the_one_furthest_from_its_own()
    {
        using var after = Decode(DefectForge.Draw(Defect.Colour, Picture(Terracotta), Swatches)!);

        AssertNear(Navy, after.GetPixel(100, 100));
    }

    /// <summary>
    /// The object's shading is kept: where it was lighter it still is, so what
    /// comes out is the product in another colour and not a flat patch of it.
    /// </summary>
    [Fact]
    public void A_recoloured_object_keeps_its_light_and_shade()
    {
        var good = Picture(with: canvas =>
        {
            using var lit = new SKPaint { Color = new SKColor(0x45, 0xA0, 0x57), IsAntialias = false };
            canvas.DrawRect(SKRect.Create(60, 60, 80, 40), lit);
        });

        using var after = Decode(DefectForge.Draw(Defect.Colour, good, Swatches)!);
        var (lit, shaded) = (after.GetPixel(100, 70), after.GetPixel(100, 130));

        Assert.True(lit.Red > shaded.Red + 20, $"lit {lit}, shaded {shaded}");
        Assert.True(lit.Red > lit.Green && lit.Green > lit.Blue, $"not a terracotta: {lit}");
        Assert.True(shaded.Red > shaded.Green && shaded.Green > shaded.Blue, $"not a terracotta: {shaded}");
    }

    /// <summary>A grey object has no hue to turn: there is no colour defect to draw, and the forge says so instead of handing back the same picture as a bad one.</summary>
    [Fact]
    public void A_grey_object_has_no_colour_defect()
    {
        Assert.Null(DefectForge.Draw(Defect.Colour, Picture(new SKColor(0x80, 0x80, 0x80)), Swatches));
    }

    /// <summary>A grey kettle with a small green light on it is a grey kettle: a tenth of it has a hue, and that is not its colour.</summary>
    [Fact]
    public void An_object_with_barely_any_hue_has_no_colour_defect()
    {
        var good = Picture(new SKColor(0x80, 0x80, 0x80), canvas =>
        {
            using var light = new SKPaint { Color = new SKColor(0x2E, 0x6B, 0x3A), IsAntialias = false };
            canvas.DrawRect(SKRect.Create(60, 132, 80, 8), light);
        });

        Assert.Null(DefectForge.Draw(Defect.Colour, good, Swatches));
    }

    /// <summary>
    /// **What the board asks before drawing a colour defect.** Cream towels
    /// with an olive stripe: the stripe turned navy leaves cream towels, and
    /// the model that goes on calling them cream is right. What has a hue in
    /// the picture of a product declared white, cream, grey or black is its
    /// trim, so there is no colour defect to draw into it.
    /// </summary>
    [Theory]
    [InlineData("#C0643F", true)]
    [InlineData("#6B7A3A", true)]
    [InlineData("#233A5E", true)]
    [InlineData("#EFE6D2", false)]
    [InlineData("#FFFFFF", false)]
    [InlineData("#8A847C", false)]
    [InlineData("#201D1B", false)]
    public void A_colour_of_the_catalogue_has_a_hue_or_has_none(string swatch, bool hasHue)
    {
        Assert.Equal(hasHue, DefectForge.HasHue(swatch));
    }

    /// <summary>A catalogue of whites and greys has no colour to turn a product to.</summary>
    [Fact]
    public void With_no_colour_of_the_catalogue_to_turn_to_there_is_no_colour_defect()
    {
        Assert.Null(DefectForge.Draw(Defect.Colour, Picture(), ["#FFFFFF", "#8A847C"]));
    }

    /// <summary>The forge is given the catalogue's own colours, from the file that declares them.</summary>
    [Fact]
    public void The_catalogues_colours_come_with_their_swatches()
    {
        var colours = ColourLexicon.Read(Path.Combine(RepositoryRoot.Find(), "seed", "attributes.sample.json"));

        Assert.Contains("#C0643F", colours.Swatches);
        Assert.Equal("#EFE6D2", colours.Swatch("crema"));
        Assert.Null(colours.Swatch("fucsia"));
        Assert.Equal(colours.EnglishLabels.Count, colours.Swatches.Count);
    }

    /// <summary>Something is drawn besides the product, outside where the product is.</summary>
    [Fact]
    public void A_scene_defect_draws_something_beside_the_object()
    {
        var good = Picture();
        using var before = Decode(good);
        using var after = Decode(DefectForge.Draw(Defect.Scene, good, Swatches)!);

        Assert.Equal(before.GetPixel(100, 100), after.GetPixel(100, 100));
        Assert.False(IsBackground(after.GetPixel(Size / 2, Size - 4)));
    }
}
