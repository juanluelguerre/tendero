using ElGuerre.Tendero.Search.Images;

namespace ElGuerre.Tendero.SearchEval.Objects;

/// <summary>
/// One photograph's answer, scored (the camera search spec, CS-2). A detected
/// kind matches an expected one when it is the same or on its branch of the
/// category tree — a desk lamp is a lamp, a sneaker is a shoe — and the floor
/// decides what is counted, through the same rules the search applies
/// (<see cref="ObjectsInPicture"/>).
/// </summary>
/// <param name="LikeliestRight">The kind it was surest of, at any score, matches an expected one: for a photograph of one thing, the question.</param>
/// <param name="Found">Expected objects matched by something kept over the floor.</param>
/// <param name="Invented">Objects kept over the floor that match nothing expected.</param>
/// <param name="Expected">How many objects the photograph has.</param>
public sealed record ObjectsScore(bool LikeliestRight, int Found, int Invented, int Expected)
{
    public static ObjectsScore Of(
        IReadOnlyList<string> expected,
        IReadOnlyList<DetectedObject> detected,
        IReadOnlyDictionary<string, string?> parents,
        double floor)
    {
        bool Matches(string kind, string wanted) => OnBranch(kind, wanted, parents) || OnBranch(wanted, kind, parents);

        var likeliest = ObjectsInPicture.Choose(detected, floor: 0).FirstOrDefault();
        var kept = ObjectsInPicture.Choose(detected, floor);
        return new ObjectsScore(
            likeliest is not null && expected.Any(wanted => Matches(likeliest.Kind.Code, wanted)),
            expected.Count(wanted => kept.Any(found => Matches(found.Kind.Code, wanted))),
            kept.Count(found => !expected.Any(wanted => Matches(found.Kind.Code, wanted))),
            expected.Count);
    }

    /// <summary>Whether <paramref name="kind"/> is <paramref name="ancestor"/> or lies under it.</summary>
    private static bool OnBranch(string kind, string ancestor, IReadOnlyDictionary<string, string?> parents)
    {
        for (string? at = kind; at is not null; at = parents.GetValueOrDefault(at))
        {
            if (at == ancestor)
                return true;
        }

        return false;
    }
}
