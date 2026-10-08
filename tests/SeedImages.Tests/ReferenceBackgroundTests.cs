using ElGuerre.Tendero.SeedImages;
using Xunit;

namespace ElGuerre.Tendero.SeedImages.Tests;

/// <summary>
/// A picture made from another keeps the other's background (phase 13a, I-4d):
/// the model that recoloured the pannier left a grey haze above it, twice, in
/// the same place. Where the reference has only background the result gets
/// background, so the one thing that may have changed is the object.
/// </summary>
public sealed class ReferenceBackgroundTests
{
    private const int Size = 40;
    private static readonly (byte R, byte G, byte B) Paper = (250, 249, 247);
    private static readonly (byte R, byte G, byte B) Olive = (90, 110, 50);
    private static readonly (byte R, byte G, byte B) Black = (30, 30, 30);
    private static readonly (byte R, byte G, byte B) Haze = (225, 225, 222);

    private static byte[] Picture(Action<Action<int, int, (byte R, byte G, byte B)>> draw)
    {
        var rgb = new byte[Size * Size * 3];
        void Set(int row, int column, (byte R, byte G, byte B) colour)
        {
            var at = ((row * Size) + column) * 3;
            (rgb[at], rgb[at + 1], rgb[at + 2]) = colour;
        }

        for (var row = 0; row < Size; row++)
            for (var column = 0; column < Size; column++)
                Set(row, column, Paper);

        draw(Set);
        return rgb;
    }

    private static (byte, byte, byte) At(byte[] rgb, int row, int column)
    {
        var at = ((row * Size) + column) * 3;
        return (rgb[at], rgb[at + 1], rgb[at + 2]);
    }

    /// <summary>The object: a square in rows and columns 20 to 29.</summary>
    private static void Square(Action<int, int, (byte R, byte G, byte B)> set, (byte R, byte G, byte B) colour, int from = 20, int to = 29)
    {
        for (var row = from; row <= to; row++)
            for (var column = from; column <= to; column++)
                set(row, column, colour);
    }

    [Fact]
    public void What_the_model_drew_where_the_reference_has_only_background_is_background_again()
    {
        var reference = Picture(set => Square(set, Olive));
        var result = Picture(set =>
        {
            Square(set, Black);
            // A haze above the object, far from it.
            for (var column = 5; column < 35; column++)
                set(3, column, Haze);
        });

        var restored = ReferenceBackground.Restore(reference, result, Size, margin: 3);

        Assert.Equal(Paper, At(restored, 3, 20));
        Assert.Equal(Black, At(restored, 25, 25));
        Assert.Equal(At(reference, 0, 0), At(restored, 0, 0));
    }

    /// <summary>
    /// The model redraws an outline a pixel or two off: what it drew just
    /// outside the reference's object is the object's edge, and is kept — up to
    /// the margin and no further, in every direction.
    /// </summary>
    [Fact]
    public void What_it_drew_within_the_margin_of_the_object_is_kept_and_beyond_it_is_not()
    {
        var reference = Picture(set => Square(set, Olive));
        var result = Picture(set => Square(set, Black, from: 15, to: 34));

        var restored = ReferenceBackground.Restore(reference, result, Size, margin: 3);

        // Three pixels out from the reference's square, on each side and at a corner: kept.
        Assert.Equal(Black, At(restored, 17, 25));
        Assert.Equal(Black, At(restored, 32, 25));
        Assert.Equal(Black, At(restored, 25, 17));
        Assert.Equal(Black, At(restored, 25, 32));
        Assert.Equal(Black, At(restored, 17, 17));
        // Four out: background.
        Assert.Equal(Paper, At(restored, 16, 25));
        Assert.Equal(Paper, At(restored, 33, 25));
        Assert.Equal(Paper, At(restored, 25, 16));
        Assert.Equal(Paper, At(restored, 25, 33));
    }

    /// <summary>
    /// The haze reaches into the margin too. There, only what is clearly drawn
    /// stays — an outline is dark, a haze is pale — or the object keeps a grey
    /// halo as wide as the margin. Where the reference itself drew, whatever the
    /// model put is the model's, pale or not.
    /// </summary>
    [Fact]
    public void In_the_margin_a_pale_pixel_is_background_and_a_dark_one_is_the_outline()
    {
        var reference = Picture(set => Square(set, Olive));
        var result = Picture(set =>
        {
            Square(set, Black);
            set(18, 25, Haze);
            set(18, 26, Black);
            // Inside the reference's object the model drew something pale: a highlight, and its to draw.
            set(25, 25, Haze);
        });

        var restored = ReferenceBackground.Restore(reference, result, Size, margin: 3);

        Assert.Equal(Paper, At(restored, 18, 25));
        Assert.Equal(Black, At(restored, 18, 26));
        Assert.Equal(Haze, At(restored, 25, 25));
    }

    /// <summary>The soft shadow under an object is a little darker than the paper: within the tolerance it is background, beyond it it is drawn.</summary>
    [Fact]
    public void A_pixel_is_drawn_when_it_is_further_from_the_paper_than_the_tolerance()
    {
        var reference = Picture(set =>
        {
            set(10, 10, ((byte)(Paper.R - ReferenceBackground.Tolerance), Paper.G, Paper.B));
            set(30, 30, (Paper.R, Paper.G, (byte)(Paper.B - ReferenceBackground.Tolerance - 1)));
        });
        var result = Picture(set =>
        {
            set(10, 10, Black);
            set(30, 30, Black);
        });

        var restored = ReferenceBackground.Restore(reference, result, Size, margin: 0);

        Assert.Equal(Paper, At(restored, 10, 10));
        Assert.Equal(Black, At(restored, 30, 30));
    }

    [Fact]
    public void The_result_itself_is_not_written_on()
    {
        var reference = Picture(_ => { });
        var result = Picture(set => set(3, 3, Haze));

        ReferenceBackground.Restore(reference, result, Size);

        Assert.Equal(Haze, At(result, 3, 3));
    }

    [Fact]
    public void Two_pictures_of_different_sizes_are_refused()
    {
        Assert.Throws<ArgumentException>(() => ReferenceBackground.Restore(new byte[12], new byte[27], 2));
    }
}
