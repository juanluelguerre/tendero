namespace ElGuerre.Tendero.Search.Strategies;

/// <summary>An item and the fused score that placed it.</summary>
public sealed record FusedItem(string Id, double Score);

/// <summary>
/// Reciprocal Rank Fusion: several rankings of the same unit, merged by position.
///
/// **Position and never score**, which is what makes the merge possible at all: a
/// BM25 score and a cosine similarity are not on one scale, and normalising them
/// would be a tuning exercise that the golden set would then have to re-justify
/// every time either engine changed. An item's fused score is the sum, over the
/// rankings it appears in, of <c>1 / (k + position)</c> with positions counted from
/// one — so appearing near the top of both beats appearing at the top of one.
///
/// <c>k = 60</c> is the value from the paper that introduced it (Cormack, Clarke
/// and Büttcher, 2009) and the one Elasticsearch and OpenSearch default to. It
/// flattens the difference between first and fifth, which is what stops one
/// engine's confident first place from outvoting the other's agreement.
///
/// A pure function, so the order is deterministic and testable without an engine:
/// equal scores go to the better best position, then to the earlier ranking —
/// lexical is passed first, so a tie goes to the words somebody typed — and then
/// to the id, so the same inputs never produce two answers.
/// </summary>
public static class ReciprocalRankFusion
{
    public const int DefaultK = 60;

    public static IReadOnlyList<FusedItem> Fuse(IReadOnlyList<IReadOnlyList<string>> rankings, int k = DefaultK)
    {
        ArgumentNullException.ThrowIfNull(rankings);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(k);

        var fused = new Dictionary<string, (double Score, int BestPosition, int FirstRanking)>(StringComparer.Ordinal);

        for (var ranking = 0; ranking < rankings.Count; ranking++)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);

            for (var position = 0; position < rankings[ranking].Count; position++)
            {
                var id = rankings[ranking][position];

                // An id listed twice in one ranking counts once, at its best place:
                // a ranking is an order, not a vote.
                if (!seen.Add(id))
                    continue;

                var contribution = 1d / (k + position + 1);

                fused[id] = fused.TryGetValue(id, out var existing)
                    ? (existing.Score + contribution, Math.Min(existing.BestPosition, position), existing.FirstRanking)
                    : (contribution, position, ranking);
            }
        }

        return
        [
            .. fused
                .OrderByDescending(item => item.Value.Score)
                .ThenBy(item => item.Value.BestPosition)
                .ThenBy(item => item.Value.FirstRanking)
                .ThenBy(item => item.Key, StringComparer.Ordinal)
                .Select(item => new FusedItem(item.Key, item.Value.Score))
        ];
    }
}
