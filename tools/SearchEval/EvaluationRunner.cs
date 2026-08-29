using Microsoft.Extensions.DependencyInjection;
using Tendero.Search.Contracts;

namespace Tendero.SearchEval;

public sealed record QueryScore(string Query, double? NdcgAt10, double? RecallAt50, int Returned);

public sealed record CultureScore(string Culture, IReadOnlyList<QueryScore> Queries)
{
    // Media sobre las consultas puntuables. Una consulta sin juicios relevantes
    // no puntúa: contarla como 0 castigaría al motor por una anotación a medias.
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

            // El índice devuelve ids internos; el golden set anota ids del origen.
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
