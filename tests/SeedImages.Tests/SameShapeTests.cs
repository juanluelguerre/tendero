using ElGuerre.Tendero.SeedImages;
using Xunit;

namespace ElGuerre.Tendero.SeedImages.Tests;

/// <summary>
/// Whether a picture made from another is still the same drawing (phase 13a,
/// I-4d): the look a person gave the black pannier beside the olive one, as
/// arithmetic.
/// </summary>
public sealed class SameShapeTests
{
    private const int Size = 20;

    private static byte[] Picture((byte R, byte G, byte B) paper, (byte R, byte G, byte B) ink, int fromRow, int toRow, int fromColumn = 5, int toColumn = 14)
    {
        var rgb = new byte[Size * Size * 3];
        for (var row = 0; row < Size; row++)
            for (var column = 0; column < Size; column++)
            {
                var at = ((row * Size) + column) * 3;
                var inside = row >= fromRow && row <= toRow && column >= fromColumn && column <= toColumn;
                (rgb[at], rgb[at + 1], rgb[at + 2]) = inside ? ink : paper;
            }

        return rgb;
    }

    private static readonly (byte, byte, byte) Paper = (250, 249, 247);
    private static readonly (byte, byte, byte) Olive = (90, 110, 50);
    private static readonly (byte, byte, byte) Black = (30, 30, 30);

    /// <summary>Another colour in the same place is the same shape: colour is exactly what is not compared.</summary>
    [Fact]
    public void The_same_shape_in_another_colour_overlaps_wholly()
    {
        Assert.Equal(1.0, SameShape.Overlap(Picture(Paper, Olive, 5, 14), Picture(Paper, Black, 5, 14), Size));
    }

    /// <summary>Intersection over union: ten rows against the lower five of them share five of ten.</summary>
    [Fact]
    public void A_shape_that_lost_half_of_itself_overlaps_by_half()
    {
        Assert.Equal(0.5, SameShape.Overlap(Picture(Paper, Olive, 5, 14), Picture(Paper, Black, 10, 14), Size));
    }

    /// <summary>Moved, it shares only where the two still meet: rows 5–14 and 10–19 share five rows of fifteen.</summary>
    [Fact]
    public void A_shape_that_moved_overlaps_only_where_the_two_still_meet()
    {
        Assert.Equal(5 / 15.0, SameShape.Overlap(Picture(Paper, Olive, 5, 14), Picture(Paper, Black, 10, 19), Size), 9);
    }

    [Fact]
    public void Two_shapes_that_never_meet_do_not_overlap()
    {
        Assert.Equal(0.0, SameShape.Overlap(Picture(Paper, Olive, 2, 6), Picture(Paper, Black, 12, 16), Size));
    }

    /// <summary>Each shape is told against its own background: a paper a shade off is not a difference.</summary>
    [Fact]
    public void A_background_a_shade_off_is_not_a_difference()
    {
        Assert.Equal(1.0, SameShape.Overlap(Picture(Paper, Olive, 5, 14), Picture((244, 243, 239), Black, 5, 14), Size));
    }

    [Fact]
    public void Two_empty_pictures_are_the_same_nothing_and_two_sizes_are_refused()
    {
        Assert.Equal(1.0, SameShape.Overlap(Picture(Paper, Paper, 0, 0), Picture(Paper, Paper, 0, 0), Size));
        Assert.Throws<ArgumentException>(() => SameShape.Overlap(new byte[12], new byte[27], 2));
    }
}
