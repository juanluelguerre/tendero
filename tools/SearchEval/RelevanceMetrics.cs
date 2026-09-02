namespace ElGuerre.Tendero.SearchEval;

/// <summary>
/// NDCG@k and recall@k, pure functions over (ranking, judgments). They are kept
/// away from Elasticsearch on purpose: a badly implemented metric invalidates the
/// CI gate in silence, so it is tested against values worked out by hand.
/// </summary>
public static class RelevanceMetrics
{
    /// <summary>The minimum relevance for a result to count as "relevant" in recall.</summary>
    private const int RelevantFrom = 1;

    /// <summary>
    /// Normalized Discounted Cumulative Gain. Gain grows as 2^rel - 1 (one
    /// perfect result is worth far more than several mediocre ones) and is
    /// discounted by log2(position + 1), because nobody scrolls. It is normalised
    /// against the ideal ordering, so 1.0 means "impossible to order better".
    /// </summary>
    /// <returns>null when the query has no relevant judgment at all: NDCG is not
    /// defined there, and averaging in a false 0 would punish the engine for an
    /// incomplete annotation.</returns>
    public static double? NdcgAt(
        int k, IReadOnlyList<string> rankedIds, IReadOnlyDictionary<string, int> judgments)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(k);

        var idealGains = judgments.Values
            .Where(relevance => relevance > 0)
            .OrderByDescending(relevance => relevance)
            .Take(k)
            .ToList();

        if (idealGains.Count == 0)
            return null;

        var actual = 0d;
        for (var position = 0; position < Math.Min(k, rankedIds.Count); position++)
        {
            var relevance = judgments.GetValueOrDefault(rankedIds[position], 0);
            actual += Gain(relevance) / Discount(position);
        }

        var ideal = 0d;
        for (var position = 0; position < idealGains.Count; position++)
            ideal += Gain(idealGains[position]) / Discount(position);

        return actual / ideal;
    }

    /// <summary>
    /// The proportion of what is relevant that appears in the first k. It watches
    /// the retrieval stage independently of ordering: if recall falls, the
    /// problem is that things are not found, not that they are badly placed.
    /// </summary>
    public static double? RecallAt(
        int k, IReadOnlyList<string> rankedIds, IReadOnlyDictionary<string, int> judgments)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(k);

        var relevant = judgments
            .Where(judgment => judgment.Value >= RelevantFrom)
            .Select(judgment => judgment.Key)
            .ToHashSet(StringComparer.Ordinal);

        if (relevant.Count == 0)
            return null;

        var found = rankedIds.Take(k).Count(relevant.Contains);

        return (double)found / relevant.Count;
    }

    private static double Gain(int relevance) => Math.Pow(2, relevance) - 1;

    // position is 0-based; the first position is not discounted (log2(2) = 1).
    private static double Discount(int position) => Math.Log2(position + 2);
}
