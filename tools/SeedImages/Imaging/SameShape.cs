namespace ElGuerre.Tendero.SeedImages;

/// <summary>
/// Whether two pictures are of the same object in the same place (phase 13a,
/// I-4d): how much of what one draws the other draws too.
///
/// A picture made from another — the same product in another colour — is
/// supposed to be the same drawing. A person saw that it was, by eye, the first
/// time; this is that look as arithmetic, so that the fortieth is not taken on
/// trust. **Colour is not compared**: only where something is drawn and where
/// there is background, which is exactly what a recolour must not move.
///
/// It says nothing of two drawings that were never meant to be the same: two
/// products, or one product drawn twice from its description, overlap a little
/// by sitting in the middle of the frame.
/// </summary>
public static class SameShape
{
    /// <summary>
    /// The overlap at and above which two pictures are the same drawing. Set
    /// from what was measured on the finished files, and from little: the
    /// pannier in black against the olive it was made from shares 0.940 — the
    /// rest is the soft shadow and the edge's antialiasing — while two different
    /// bags of the catalogue share 0.632 and 0.424. The line sits between, nearer
    /// the pictures that are the same.
    /// </summary>
    public const double Enough = 0.90;

    /// <summary>
    /// The share of what either picture draws that both draw: intersection over
    /// union of the two shapes, 1 for the same shape and 0 for two that never
    /// meet. Each shape is told against its own picture's background, so a
    /// background a shade off does not count as a difference.
    /// </summary>
    public static double Overlap(byte[] first, byte[] second, int size, int tolerance = ReferenceBackground.Tolerance)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);
        if (first.Length != size * size * 3 || second.Length != first.Length)
            throw new ArgumentException("The two are the same square picture, three bytes a pixel.");

        var (both, either) = (0, 0);
        for (var pixel = 0; pixel < size * size; pixel++)
        {
            var (inFirst, inSecond) = (Drawn(first, pixel, tolerance), Drawn(second, pixel, tolerance));
            both += inFirst && inSecond ? 1 : 0;
            either += inFirst || inSecond ? 1 : 0;
        }

        // Two empty pictures draw the same nothing.
        return either == 0 ? 1 : both / (double)either;
    }

    /// <summary>Whether a pixel is further from its picture's background — the corner's colour — than the tolerance.</summary>
    private static bool Drawn(byte[] picture, int pixel, int tolerance)
    {
        var at = pixel * 3;
        return Math.Abs(picture[at] - picture[0]) > tolerance
            || Math.Abs(picture[at + 1] - picture[1]) > tolerance
            || Math.Abs(picture[at + 2] - picture[2]) > tolerance;
    }
}
