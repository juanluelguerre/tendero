namespace ElGuerre.Tendero.SeedImages;

/// <summary>
/// The background of a picture that was made from another (phase 13a, I-4d).
///
/// A model asked to give a picture back in another colour keeps the object and
/// **does not keep the background**: twice running, the black pannier came back
/// with a grey haze above it, in the same place, where the olive one has plain
/// background. Asking again does not move it. But the reference says where the
/// background is — so wherever the reference has only background, the result
/// gets background, and what the model was free to change is the object alone.
///
/// The reference's object is grown by a margin first: the model redraws an
/// outline a pixel or two off, and a mask cut to the exact edge would shave it.
/// </summary>
public static class ReferenceBackground
{
    /// <summary>How far a pixel may be from the background colour, per channel, and still be background: the soft shadow under an object is not.</summary>
    public const int Tolerance = 10;

    /// <summary>How many pixels the reference's object is grown by before the rest is called background.</summary>
    public const int Margin = 10;

    /// <summary>
    /// How far from the background a pixel in that margin has to be to stay. The
    /// margin is there for an outline redrawn a pixel off, and an outline is
    /// dark; the haze reaches into the margin too, and is pale. Without this the
    /// object came back with a grey halo the width of the margin.
    /// </summary>
    public const int InTheMargin = 70;

    /// <param name="reference">The picture the result was made from: interleaved RGB, <paramref name="size"/> square.</param>
    /// <param name="result">What the model gave back, the same size.</param>
    /// <returns>The result, with the reference's background wherever the reference has nothing drawn within the margin.</returns>
    public static byte[] Restore(byte[] reference, byte[] result, int size, int margin = Margin, int tolerance = Tolerance)
    {
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentNullException.ThrowIfNull(result);
        if (reference.Length != size * size * 3 || result.Length != reference.Length)
            throw new ArgumentException("The reference and the result are the same square picture, three bytes a pixel.");

        // The background is what the corner is: every picture of the catalogue has its object in the middle.
        var (red, green, blue) = (reference[0], reference[1], reference[2]);

        var drawn = new bool[size * size];
        for (var pixel = 0; pixel < drawn.Length; pixel++)
        {
            var at = pixel * 3;
            drawn[pixel] = Math.Abs(reference[at] - red) > tolerance
                || Math.Abs(reference[at + 1] - green) > tolerance
                || Math.Abs(reference[at + 2] - blue) > tolerance;
        }

        var kept = Grown(Grown(drawn, size, margin, alongRows: true), size, margin, alongRows: false);

        var restored = (byte[])result.Clone();
        for (var pixel = 0; pixel < kept.Length; pixel++)
        {
            var at = pixel * 3;

            // Where the reference drew, the result is the model's. In the margin
            // around it, only what is clearly drawn — an outline, not a haze.
            if (drawn[pixel]
                || (kept[pixel] && (Math.Abs(result[at] - red) > InTheMargin
                    || Math.Abs(result[at + 1] - green) > InTheMargin
                    || Math.Abs(result[at + 2] - blue) > InTheMargin)))
            {
                continue;
            }

            (restored[at], restored[at + 1], restored[at + 2]) = (red, green, blue);
        }

        return restored;
    }

    /// <summary>The mask grown by <paramref name="margin"/> pixels along one direction; twice, once each way, grows it by a square.</summary>
    private static bool[] Grown(bool[] mask, int size, int margin, bool alongRows)
    {
        var grown = new bool[mask.Length];

        for (var line = 0; line < size; line++)
        {
            // How far back the last drawn pixel of this line is, as the line is walked both ways.
            for (var pass = 0; pass < 2; pass++)
            {
                var since = int.MaxValue;
                for (var step = 0; step < size; step++)
                {
                    var along = pass == 0 ? step : size - 1 - step;
                    var index = alongRows ? (line * size) + along : (along * size) + line;

                    since = mask[index] ? 0 : since == int.MaxValue ? since : since + 1;
                    if (since <= margin)
                        grown[index] = true;
                }
            }
        }

        return grown;
    }
}
