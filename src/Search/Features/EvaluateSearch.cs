using System.Diagnostics;
using Carter;
using ElGuerre.Tendero.Search.Contracts;
using ElGuerre.Tendero.Search.Evaluation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace ElGuerre.Tendero.Search.Features.EvaluateSearch;

/// <summary>
/// The search evaluation panel's question (P8-11): with this shop's catalogue and
/// indexes as they are right now, how do lexical and hybrid search score against
/// the committed golden sets — side by side, query by query.
///
/// It is the CI gate's measurement pointed at the running stack instead of at a
/// throwaway corpus, and that difference is the reason it exists. The gate proves
/// a pull request did not make relevance worse; this shows whether the shop a
/// person is looking at — its drafts unpublished, its vector layer up or down —
/// actually gets what the gate measured. Both use <see cref="RelevanceEvaluator"/>
/// and the same embedded golden sets, so they cannot disagree about the metric.
///
/// It asks BOTH strategies whatever the SearchVector flag says: the panel exists to
/// compare them, and the flag decides what the storefront gets, not what a
/// shopkeeper may measure. With the vector layer down, hybrid's column equals
/// lexical's — the degradation working — and the answer says why beside it.
/// </summary>
public sealed record EvaluateSearchQuery : IQuery<SearchEvaluation>;

public sealed record SearchEvaluation(
    VectorLayerReport VectorLayer,
    int JudgedProductsInCatalogue,
    IReadOnlyList<CultureEvaluation> Cultures,
    double ElapsedSeconds);

public sealed record VectorLayerReport(bool IsConfigured, bool IsAvailable, string Reason);

public sealed record CultureEvaluation(
    string Culture,
    StrategyScore Lexical,
    StrategyScore Hybrid,
    IReadOnlyList<QueryEvaluation> Queries);

public sealed record StrategyScore(
    double NdcgAt10,
    double RecallAt50,
    double ThresholdNdcgAt10,
    double ThresholdRecallAt50,
    bool MeetsThresholds,
    double MeanMilliseconds);

public sealed record QueryEvaluation(
    string Query,
    double? LexicalNdcgAt10,
    double? HybridNdcgAt10,
    double? LexicalRecallAt50,
    double? HybridRecallAt50);

public sealed class EvaluateSearchEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        // GET /api/search/evaluation — forty-four queries, twice. Seconds, not
        // milliseconds, which is why the panel asks for it on a button.
        app.MapGet("/api/search/evaluation",
                async Task<Ok<SearchEvaluation>> (IQueryDispatcher dispatcher, CancellationToken ct) =>
                    TypedResults.Ok(await dispatcher.SendAsync(new EvaluateSearchQuery(), ct)))
            .RequireAuthorization(TenderoPolicyNames.Shopkeeper)
            .WithTags("Search")
            .WithName("EvaluateSearch");
    }
}

public sealed class EvaluateSearchHandler(
    IProductReader products,
    [FromKeyedServices(SearchStrategies.Lexical)] ISearchStrategy lexical,
    [FromKeyedServices(SearchStrategies.Hybrid)] ISearchStrategy hybrid,
    IVectorLayer vectorLayer) : IQueryHandler<EvaluateSearchQuery, SearchEvaluation>
{
    private static readonly ActivitySource Telemetry = new(TelemetrySources.Search);

    public async Task<SearchEvaluation> HandleAsync(EvaluateSearchQuery query, CancellationToken ct)
    {
        using var activity = Telemetry.StartActivity("search.evaluate");
        var stopwatch = Stopwatch.StartNew();

        var status = await vectorLayer.CheckAsync(ct);
        var goldenSets = await EmbeddedEvaluation.GoldenSetsAsync(ct);
        var thresholds = await EmbeddedEvaluation.ThresholdsAsync(ct);

        // Golden sets judge SOURCE ids and the index answers with internal ones,
        // so the catalogue is walked once to translate — and whatever it holds
        // from that source is what can be judged at all. An empty catalogue scores
        // zero everywhere, and the count lets the panel say that instead.
        var judged = goldenSets
            .SelectMany(set => set.Queries.SelectMany(goldenQuery => goldenQuery.Judgments.Select(judgment => (set.Source, judgment.ExternalId))))
            .ToHashSet();

        var externalIds = new Dictionary<string, string>(StringComparer.Ordinal);
        var judgedInCatalogue = 0;

        await foreach (var product in products.StreamAllAsync(ct))
        {
            foreach (var reference in product.ExternalReferences.Where(reference => goldenSets.Any(set => set.Source == reference.Source)))
            {
                externalIds[product.Id.ToString()] = reference.ExternalId;

                if (judged.Contains((reference.Source, reference.ExternalId)))
                    judgedInCatalogue++;
            }
        }

        var cultures = new List<CultureEvaluation>(goldenSets.Count);

        foreach (var golden in goldenSets)
        {
            var lexicalScore = await new RelevanceEvaluator(lexical).RunAsync(golden, externalIds, ct);
            var hybridScore = await new RelevanceEvaluator(hybrid).RunAsync(golden, externalIds, ct);

            cultures.Add(new CultureEvaluation(
                golden.Culture,
                Summarise(lexicalScore, thresholds.For(SearchStrategies.Lexical, golden.Culture)),
                Summarise(hybridScore, thresholds.For(SearchStrategies.Hybrid, golden.Culture)),
                [.. lexicalScore.Queries.Zip(hybridScore.Queries, (words, meaning) => new QueryEvaluation(
                    words.Query, words.NdcgAt10, meaning.NdcgAt10, words.RecallAt50, meaning.RecallAt50))]));
        }

        activity?.SetTag("search.evaluate.judged_products", judgedInCatalogue);
        activity?.SetTag("search.evaluate.vector_layer", status.IsAvailable);

        return new SearchEvaluation(
            new VectorLayerReport(status.IsConfigured, status.IsAvailable, status.Reason),
            judgedInCatalogue,
            cultures,
            stopwatch.Elapsed.TotalSeconds);
    }

    private static StrategyScore Summarise(CultureScore score, CultureThresholds thresholds) =>
        new(score.MeanNdcgAt10, score.MeanRecallAt50, thresholds.NdcgAt10, thresholds.RecallAt50,
            thresholds.AreMetBy(score), score.MeanMilliseconds);
}
