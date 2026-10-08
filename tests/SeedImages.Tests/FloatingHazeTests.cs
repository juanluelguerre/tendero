using ElGuerre.Tendero.SeedImages;
using Xunit;

namespace ElGuerre.Tendero.SeedImages.Tests;

/// <summary>
/// The grey smear an edited picture comes back with above its object (phase
/// 13a, I-4d). Another view has no original to be cleaned against, but every
/// picture of the catalogue is an object and at most a soft shadow under it: so
/// what is pale and above the object goes, and the object and its shadow stay.
/// </summary>
public sealed class FloatingHazeTests
{
    private const int Size = 200;
    private static readonly (byte R, byte G, byte B) Paper = (250, 249, 247);
    private static readonly (byte R, byte G, byte B) Olive = (90, 110, 50);
    private static readonly (byte R, byte G, byte B) Haze = (225, 225, 222);
    private static readonly (byte R, byte G, byte B) Shadow = (232, 231, 228);

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

    /// <summary>An object in rows 60 to 159 and columns 60 to 139.</summary>
    private static void Object(Action<int, int, (byte R, byte G, byte B)> set)
    {
        for (var row = 60; row <= 159; row++)
            for (var column = 60; column <= 139; column++)
                set(row, column, Olive);
    }

    [Fact]
    public void What_is_pale_and_floats_above_the_object_goes_and_the_object_stays()
    {
        var picture = Picture(set =>
        {
            Object(set);
            for (var column = 40; column < 160; column++)
                for (var row = 20; row < 40; row++)
                    set(row, column, Haze);
        });

        var cleared = FloatingHaze.Clear(picture, Size);

        Assert.Equal(Paper, At(cleared, 30, 100));
        Assert.Equal(Paper, At(cleared, 30, 45));
        Assert.Equal(Olive, At(cleared, 60, 100));
        Assert.Equal(Olive, At(cleared, 159, 139));
    }

    /// <summary>The object's own pale edge — a pixel or two of antialiasing above its outline — is not haze.</summary>
    [Fact]
    public void A_pale_pixel_just_above_the_objects_top_is_its_edge()
    {
        var picture = Picture(set =>
        {
            Object(set);
            set(60 - FloatingHaze.Above, 100, Haze);
            set(60 - FloatingHaze.Above - 1, 100, Haze);
        });

        var cleared = FloatingHaze.Clear(picture, Size);

        Assert.Equal(Haze, At(cleared, 60 - FloatingHaze.Above, 100));
        Assert.Equal(Paper, At(cleared, 60 - FloatingHaze.Above - 1, 100));
    }

    /// <summary>
    /// The shadow is pale too, and stays: under the object, and beside it where
    /// it spreads past the object's edge — near the foot, not higher.
    /// </summary>
    [Fact]
    public void The_shadow_under_and_beside_the_objects_foot_stays()
    {
        var picture = Picture(set =>
        {
            Object(set);
            for (var column = 40; column < 160; column++)
                for (var row = 160; row < 170; row++)
                    set(row, column, Shadow);
            // Beside the object and far above its foot: not a shadow.
            set(159 - FloatingHaze.ShadowReach - 1, 45, Haze);
            set(159 - FloatingHaze.ShadowReach, 45, Haze);
        });

        var cleared = FloatingHaze.Clear(picture, Size);

        Assert.Equal(Shadow, At(cleared, 165, 100));
        Assert.Equal(Shadow, At(cleared, 165, 45));
        Assert.Equal(Paper, At(cleared, 159 - FloatingHaze.ShadowReach - 1, 45));
        Assert.Equal(Haze, At(cleared, 159 - FloatingHaze.ShadowReach, 45));
    }

    [Fact]
    public void A_picture_with_nothing_drawn_is_left_as_it_is_and_the_picture_given_is_not_written_on()
    {
        var empty = Picture(set => set(30, 30, Haze));
        var withObject = Picture(set =>
        {
            Object(set);
            set(10, 100, Haze);
        });

        Assert.Equal(Haze, At(FloatingHaze.Clear(empty, Size), 30, 30));
        FloatingHaze.Clear(withObject, Size);
        Assert.Equal(Haze, At(withObject, 10, 100));
    }
}
