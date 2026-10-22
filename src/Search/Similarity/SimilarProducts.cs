using ElGuerre.Tendero.Search.Contracts;

namespace ElGuerre.Tendero.Search.Similarity;

/// <summary>A product suggested because another was viewed, and how many attributes the two share.</summary>
/// <param name="BecauseOf">The code of the viewed product it comes from — the *why*, as data.</param>
public sealed record Suggestion(ProductProfile Product, string BecauseOf, int Shared);

/// <summary>The candidates the rule found for one viewed product, best first.</summary>
public sealed record Neighbourhood(ProductProfile Viewed, IReadOnlyList<Suggestion> Candidates);

/// <summary>
/// *Puede que te guste* (R-8), decided by code first (invariant 12): what the
/// shopper viewed says where to look, and the attributes say what is alike.
///
/// Three steps, each a pure function, so each is tested with no index and no
/// model, and the one that needs the vector layer — the re-rank — sits between
/// two that never do:
///
/// <list type="number">
/// <item><see cref="Neighbourhoods"/>: for each viewed product, the products in
/// its category, ranked by how many attributes they share with it — never one
/// that was viewed, never one sold out.</item>
/// <item><see cref="Reorder"/>: with the vector layer up, a neighbourhood
/// re-sorted by how close each candidate's embedding is to the viewed one's.
/// Without it this step is skipped and the rule's order stands.</item>
/// <item><see cref="Spread"/>: the row, taken in turns from each viewed product
/// so the first one does not fill it alone.</item>
/// </list>
/// </summary>
public static class SimilarProducts
{
    /// <summary>
    /// How many ATTRIBUTES two products agree on — a code on which they share at
    /// least one value — not how many values. Counting values would let sizes
    /// decide: two shoes in 38 to 42 share five values and one fact, and the
    /// shared material, which is what a shopper means by "like this", would be
    /// outvoted by the size chart.
    /// </summary>
    public static int SharedAttributes(ProductProfile viewed, ProductProfile candidate)
    {
        var theirs = candidate.Traits.ToHashSet(StringComparer.Ordinal);

        return viewed.Traits
            .Where(theirs.Contains)
            .Select(CodeOf)
            .Distinct(StringComparer.Ordinal)
            .Count();
    }

    /// <summary>
    /// Each viewed product's neighbours, in the order the products were viewed: the
    /// pool's products in the same leaf category, ranked by
    /// <see cref="SharedAttributes"/>, then by code so the same pool always ranks
    /// the same way. A product somebody viewed is never suggested — they have
    /// seen it — and one that cannot be bought is not either: a suggestion that
    /// ends in "sold out" is a dead end on the home.
    /// </summary>
    public static IReadOnlyList<Neighbourhood> Neighbourhoods(IReadOnlyList<ProductProfile> viewed, IReadOnlyList<ProductProfile> pool)
    {
        var seen = viewed.Select(product => product.ProductId).ToHashSet(StringComparer.Ordinal);

        return
        [
            .. viewed.Select(product => new Neighbourhood(product,
            [
                .. pool
                    .Where(candidate => product.Category is not null
                                        && String.Equals(candidate.Category, product.Category, StringComparison.Ordinal)
                                        && !seen.Contains(candidate.ProductId)
                                        && candidate.InStock)
                    .Select(candidate => new Suggestion(candidate, product.Code, SharedAttributes(product, candidate)))
                    .OrderByDescending(suggestion => suggestion.Shared)
                    .ThenBy(suggestion => suggestion.Product.Code, StringComparer.Ordinal)
            ]))
        ];
    }

    /// <summary>
    /// A neighbourhood re-sorted by embedding similarity, nearest first. A
    /// candidate the vector layer has no vector for keeps its place behind the
    /// ones it had, in the rule's order, and equal similarities keep the rule's
    /// order too — so the re-rank only ever moves what it has evidence about.
    /// </summary>
    public static Neighbourhood Reorder(Neighbourhood neighbourhood, IReadOnlyDictionary<string, double> similarity) =>
        neighbourhood with
        {
            Candidates =
            [
                .. neighbourhood.Candidates
                    .Select((candidate, position) => (Candidate: candidate, Position: position,
                        Similarity: similarity.TryGetValue(candidate.Product.ProductId, out var value) ? value : Double.NegativeInfinity))
                    .OrderByDescending(ranked => ranked.Similarity)
                    .ThenBy(ranked => ranked.Position)
                    .Select(ranked => ranked.Candidate)
            ]
        };

    /// <summary>
    /// The row: the first of each neighbourhood, then the second of each, and so
    /// on, until <paramref name="limit"/> — spread across what was viewed rather
    /// than eight neighbours of the first product. A product two viewed products
    /// both reach is suggested once, for the one whose turn came first.
    /// </summary>
    public static IReadOnlyList<Suggestion> Spread(IReadOnlyList<Neighbourhood> neighbourhoods, int limit)
    {
        var row = new List<Suggestion>(limit);
        var taken = new HashSet<string>(StringComparer.Ordinal);
        var deepest = neighbourhoods.Count == 0 ? 0 : neighbourhoods.Max(neighbourhood => neighbourhood.Candidates.Count);

        for (var turn = 0; turn < deepest && row.Count < limit; turn++)
        {
            foreach (var neighbourhood in neighbourhoods)
            {
                if (row.Count == limit)
                    break;

                // A neighbourhood whose next candidate was taken by an earlier one
                // passes its turn rather than reaching deeper: turns stay fair.
                if (turn < neighbourhood.Candidates.Count && taken.Add(neighbourhood.Candidates[turn].Product.ProductId))
                    row.Add(neighbourhood.Candidates[turn]);
            }
        }

        return row;
    }

    private static string CodeOf(string trait) =>
        trait.IndexOf(':', StringComparison.Ordinal) is var colon and > 0 ? trait[..colon] : trait;
}
