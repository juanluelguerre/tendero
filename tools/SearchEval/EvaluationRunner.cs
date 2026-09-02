using ElGuerre.Tendero.Search.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace ElGuerre.Tendero.SearchEval;

public sealed record QueryScore(string Query, double? NdcgAt10, double? RecallAt50, int Returned);

public sealed record CultureScore(string Culture, IReadOnlyList<QueryScore> Queries)
{
    // The mean over the scoreable queries. A query with no relevant judgments
    // does not score: counting it as 0 would punish the engine for a half-written
    // annotation.
    public double MeanNdcgAt10 => Mean(Queries.Select(query => query.NdcgAt10));
    public double MeanRecallAt50 => Mean(Queries.Select(query => query.RecallAt50));
    public int ScoredQueries => Queries.Count(query => query.NdcgAt10 is not null);

    private static double Mean(IEnumerable<double?> values)
    {
        var scored = values.OfType<double>().ToList();
        return scored.Count == 0 ? 0d : scored.Average();
    }
}

public sealed class EvaluationRunner(IServiceProvider services)
{
    private const int NdcgCutoff = 10;
    private const int RecallCutoff = 50;

    public async Task<CultureScore> RunAsync(
        GoldenSet golden,
        IReadOnlyDictionary<string, string> externalIdByInternalId,
        CancellationToken cancellationToken)
    {
        var search = services.GetRequiredService<ILexicalProductSearch>();
        var scores = new List<QueryScore>(golden.Queries.Count);

        foreach (var goldenQuery in golden.Queries)
        {
            var page = await search.SearchAsync(
                new ProductSearchQuery(goldenQuery.Query, golden.Culture, Page: 1, PageSize: RecallCutoff),
                cancellationToken);

            // The index returns internal ids; the golden set annotates source ids.
            var ranked = page.Hits
                .Select(hit => externalIdByInternalId.GetValueOrDefault(hit.ProductId))
                .OfType<string>()
                .ToList();

            var judgments = goldenQuery.ToRelevanceMap();

            scores.Add(new QueryScore(
                goldenQuery.Query,
                RelevanceMetrics.NdcgAt(NdcgCutoff, ranked, judgments),
                RelevanceMetrics.RecallAt(RecallCutoff, ranked, judgments),
                ranked.Count));
        }

        return new CultureScore(golden.Culture, scores);
    }
}
