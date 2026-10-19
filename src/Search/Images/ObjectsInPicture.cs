namespace ElGuerre.Tendero.Search.Images;

/// <summary>
/// What the search keeps of whatever a detector found (the camera search spec,
/// CS-2): one object a kind, none under the floor, eight at most, the likeliest
/// first. One place for it, so every detector is measured and shown under the
/// same rules and none of them carries its own.
/// </summary>
public static class ObjectsInPicture
{
    /// <summary>More blocks than this is a page nobody reads, and a picture with more is not a question about one thing.</summary>
    public const int MaxObjects = 8;

    /// <param name="found">The detector's raw answer.</param>
    /// <param name="floor">The score under which a box is not counted, chosen from the picture suite's measurement.</param>
    public static IReadOnlyList<DetectedObject> Choose(IEnumerable<DetectedObject> found, double floor) =>
        [.. found
            .Where(detected => detected.Score >= floor)
            .GroupBy(detected => detected.Kind)
            .Select(kind => kind.MaxBy(detected => detected.Score)!)
            .OrderByDescending(detected => detected.Score)
            .Take(MaxObjects)];

    /// <summary>
    /// The objects as the picture is read (CS-3): by the third of its height each
    /// one's centre is in, then left to right. A strict top-to-bottom order would
    /// swap two things side by side whenever one sat a pixel higher.
    /// </summary>
    public static IReadOnlyList<DetectedObject> InReadingOrder(IEnumerable<DetectedObject> objects, int pictureHeight) =>
        [.. objects
            .OrderBy(found => Math.Min(2, (found.Box.Top + (found.Box.Height / 2)) * 3 / Math.Max(1, pictureHeight)))
            .ThenBy(found => found.Box.Left + (found.Box.Width / 2))];
}
