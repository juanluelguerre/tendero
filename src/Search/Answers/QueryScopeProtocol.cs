namespace ElGuerre.Tendero.Search.Answers;

/// <summary>
/// What one run of layer B counted over a set of scope queries at one margin.
/// </summary>
/// <param name="Margin">The margin the classifier ran with.</param>
/// <param name="OutRecognised">Queries labelled <c>out</c> that were answered out of scope.</param>
/// <param name="OutTotal">Queries labelled <c>out</c>.</param>
/// <param name="InMarkedOut">
/// Queries the shop CAN answer that were told it sells nothing for them — the
/// failure the protocol does not negotiate on.
/// </param>
/// <remarks>
/// Public rather than internal because the measurement tool counts with it through
/// <c>QueryScopeEvaluation</c>; it is a plain record of four numbers, and exposing
/// it exposes nothing of how layer B decides.
/// </remarks>
public sealed record MarginResult(double Margin, int OutRecognised, int OutTotal, int InMarkedOut);

/// <summary>
/// The selection and acceptance rules of layer B's protocol, declared in the
/// notebook on 2026-09-17 BEFORE anything was measured, and kept pure so the
/// rules a run is judged by are proven before the run exists.
/// </summary>
internal static class QueryScopeProtocol
{
    /// <summary>The grid declared in advance. It is not tuned after a run.</summary>
    public static readonly double[] Grid = [0.02, 0.05, 0.08, 0.12];

    /// <summary>
    /// One margin for the shop, not one per culture: both cultures' counts are
    /// summed per margin, so a margin that tells one Spanish shopper "we don't sell
    /// that" is refused even when it looks perfect in English. Ordered by margin.
    /// </summary>
    public static IReadOnlyList<MarginResult> Combine(IEnumerable<MarginResult> perCulture) =>
    [
        .. perCulture
            .GroupBy(result => result.Margin)
            .OrderBy(group => group.Key)
            .Select(group => new MarginResult(
                group.Key,
                group.Sum(result => result.OutRecognised),
                group.Sum(result => result.OutTotal),
                group.Sum(result => result.InMarkedOut)))
    ];

    /// <summary>The margin with the most out-of-scope queries recognised among those marking no in-scope query out; ties to the larger margin. Null when every margin marks an in-scope query out.</summary>
    public static double? Choose(IReadOnlyList<MarginResult> dev) =>
        dev
            .Where(result => result.InMarkedOut == 0)
            .OrderByDescending(result => result.OutRecognised)
            .ThenByDescending(result => result.Margin)
            .Select(result => (double?)result.Margin)
            .FirstOrDefault();

    /// <summary>Zero in-scope marked out in both test sets and both golden sets, and at least half of each culture's out queries recognised.</summary>
    public static bool Accepts(IReadOnlyList<MarginResult> scopeTestByCulture, IReadOnlyList<int> goldenInMarkedOutByCulture) =>
        scopeTestByCulture.Count > 0
        && goldenInMarkedOutByCulture.Count > 0
        && scopeTestByCulture.All(result => result.InMarkedOut == 0 && 2 * result.OutRecognised >= result.OutTotal)
        && goldenInMarkedOutByCulture.All(count => count == 0);
}
