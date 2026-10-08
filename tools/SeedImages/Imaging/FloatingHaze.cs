namespace ElGuerre.Tendero.SeedImages;

/// <summary>
/// What an edited picture has floating above its object (phase 13a, I-4d).
///
/// The hosted model, shown a picture and asked to change it, gives the object
/// back and a pale grey smear over the background above it — every time so far,
/// a recolour or another view, and asking for a clean background does not move
/// it. A recolour is cleaned against its original (<see cref="ReferenceBackground"/>).
/// Another view has no original to be held to: it is a different shape. But
/// every picture of the catalogue has the same anatomy — an object, drawn
/// strongly, and at most a soft shadow **under** it — so anything pale that
/// floats above the object is not the picture's.
///
/// Column by column: above the topmost strongly drawn pixel, pale goes back to
/// background. Beside the object, where a column has nothing strong, pale is
/// kept only near the object's foot, where a shadow spreads.
/// </summary>
public static class FloatingHaze
{
    /// <summary>How close above the object's top a pale pixel may sit and still be its edge.</summary>
    public const int Above = 6;

    /// <summary>How far up from the object's lowest row a shadow may reach beside it.</summary>
    public const int ShadowReach = 120;

    public static byte[] Clear(byte[] picture, int size, int strong = ReferenceBackground.InTheMargin)
    {
        ArgumentNullException.ThrowIfNull(picture);
        if (picture.Length != size * size * 3)
            throw new ArgumentException("A square picture, three bytes a pixel.");

        // The background is what the corner is, as everywhere in this tool.
        var (red, green, blue) = (picture[0], picture[1], picture[2]);
        int Far(int pixel)
        {
            var at = pixel * 3;
            return Math.Max(Math.Abs(picture[at] - red), Math.Max(Math.Abs(picture[at + 1] - green), Math.Abs(picture[at + 2] - blue)));
        }

        // The topmost strongly drawn row of each column, and the object's lowest row.
        var top = new int[size];
        var foot = -1;
        for (var column = 0; column < size; column++)
        {
            top[column] = -1;
            for (var row = 0; row < size; row++)
            {
                if (Far((row * size) + column) <= strong)
                    continue;

                if (top[column] < 0)
                    top[column] = row;
                foot = Math.Max(foot, row);
            }
        }

        var cleared = (byte[])picture.Clone();
        if (foot < 0)
            return cleared;

        for (var column = 0; column < size; column++)
        {
            // Where the picture's own lines begin in this column; beside the object, where a shadow may.
            var from = top[column] >= 0 ? top[column] - Above : foot - ShadowReach;

            for (var row = 0; row < Math.Min(from, size); row++)
            {
                var at = ((row * size) + column) * 3;
                (cleared[at], cleared[at + 1], cleared[at + 2]) = (red, green, blue);
            }
        }

        return cleared;
    }
}
