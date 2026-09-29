using System.Diagnostics;
using Carter;
using ElGuerre.Tendero.Catalog.Domain;
using ElGuerre.Tendero.Search.Contracts;
using ElGuerre.Tendero.Search.Similarity;
using ElGuerre.Tendero.Search.Strategies;
using ElGuerre.Tendero.SharedKernel;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ElGuerre.Tendero.Search.Features.SuggestSimilar;

/// <summary>
/// What ordered a row of suggestions: the rule alone, or the rule's candidates
/// re-sorted by their embeddings. A closed set (ADR 0043), and on the wire so the
/// spec's decision can be measured — "if the vector re-rank does not beat the
/// rule alone, it is switched off" needs to know which one a shopper saw.
/// </summary>
public enum SimilarityRanking
{
    /// <summary>Category and shared attributes: the answer with no vector layer, and whenever it fails.</summary>
    Rule,

    /// <summary>The same candidates, nearest in meaning first.</summary>
    Embeddings,
}

/// <param name="Product">The card, as a search result draws it.</param>
/// <param name="BecauseOf">The code of the viewed product it comes from: the page says *Porque viste …* from this, not from wording of its own.</param>
public sealed record SimilarProduct(SearchHit Product, string BecauseOf);

/// <param name="Items">Empty when nothing viewed has a neighbour that can be bought — the page then draws no row.</param>
public sealed record SimilarProductsResult(IReadOnlyList<SimilarProduct> Items, SimilarityRanking RankedBy);

public sealed record SuggestSimilarQuery(IReadOnlyList<string> Codes, string Culture, int Limit) : IQuery<SimilarProductsResult>;

public sealed class SuggestSimilarValidator : AbstractValidator<SuggestSimilarQuery>
{
    /// <summary>The trail's row holds eight products; more would be a profile, not a trail.</summary>
    public const int MaxViewed = 8;

    public const int DefaultLimit = 8;

    public const int MaxLimit = 24;

    public SuggestSimilarValidator()
    {
        RuleFor(query => query.Codes).NotEmpty().OverridePropertyName("codes");
        RuleFor(query => query.Codes.Count).LessThanOrEqualTo(MaxViewed).OverridePropertyName("codes");
        RuleForEach(query => query.Codes)
            .Must(code => code is not null && ProductCode.IsWellFormed(ProductCode.Normalise(code)))
            .WithMessage($"A product is its public code: {ProductCode.Length} letters and digits.")
            .OverridePropertyName("codes");

        RuleFor(query => query.Culture).Must(culture => culture is "es" or "en").WithMessage("Supported cultures: es, en.");
        RuleFor(query => query.Limit).InclusiveBetween(1, MaxLimit).OverridePropertyName("limit");
    }
}

public sealed class SuggestSimilarEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        // GET /api/search/similar?codes=B073WXYZ01&codes=B07C0FFEE1&culture=es&limit=8
        app.MapGet("/api/search/similar",
            async Task<Ok<SimilarProductsResult>> (
                   string[]? codes, string? culture, int? limit,
                   HttpContext http, IQueryDispatcher dispatcher, CancellationToken ct) =>
            {
                var resolved = CultureNegotiation.Resolve(culture, http.Request.Headers.AcceptLanguage);

                var result = await dispatcher.SendAsync(
                    new SuggestSimilarQuery(codes ?? [], resolved, limit ?? SuggestSimilarValidator.DefaultLimit), ct);

                http.Response.Headers.ContentLanguage = resolved;
                http.Response.Headers.Vary = "Accept-Language";

                return TypedResults.Ok(result);
            })
            // The codes are the browser's own trail, sent by the page; nothing
            // about the shopper is read or kept here, so a guest asks as a customer does.
            .AllowAnonymous()
            .WithTags("Search")
            .WithName("SuggestSimilarProducts");
    }
}

public sealed class SuggestSimilarHandler(
    IProductProfileReader profiles,
    IVectorSimilarity similarity,
    IOptions<HybridSearchOptions> options,
    ILogger<SuggestSimilarHandler> logger)
    : IQueryHandler<SuggestSimilarQuery, SimilarProductsResult>
{
    private static readonly ActivitySource Telemetry = new(TelemetrySources.Search);

    public async Task<SimilarProductsResult> HandleAsync(SuggestSimilarQuery query, CancellationToken ct)
    {
        using var activity = Telemetry.StartActivity("search.similar");
        activity?.SetTag("search.culture", query.Culture);

        var codes = query.Codes.Select(ProductCode.Normalise).Distinct(StringComparer.Ordinal).ToList();
        var found = await profiles.ByCodesAsync(codes, query.Culture, ct);

        // In the trail's order — most recent first, as the page sends it — and a
        // code the index does not hold (withdrawn since it was viewed) left out.
        var viewed = codes
            .Select(code => found.FirstOrDefault(profile => string.Equals(profile.Code, code, StringComparison.Ordinal)))
            .OfType<ProductProfile>()
            .ToList();

        activity?.SetTag("search.similar.viewed", viewed.Count);

        if (viewed.Count == 0)
            return new SimilarProductsResult([], SimilarityRanking.Rule);

        var pool = await profiles.InCategoriesAsync(
            [.. viewed.Select(product => product.Category).OfType<string>().Distinct(StringComparer.Ordinal)], query.Culture, ct);

        var (neighbourhoods, rankedBy) = await RerankAsync(SimilarProducts.Neighbourhoods(viewed, pool), query.Culture, activity, ct);
        var row = SimilarProducts.Spread(neighbourhoods, query.Limit);

        activity?.SetTag("search.similar.ranked_by", rankedBy.ToString());
        activity?.SetTag("search.similar.suggested", row.Count);

        return new SimilarProductsResult([.. row.Select(suggestion => new SimilarProduct(suggestion.Product.Card, suggestion.BecauseOf))], rankedBy);
    }

    /// <summary>
    /// The rule's neighbourhoods re-sorted by meaning, within the vector budget the
    /// hybrid search keeps — or the rule's own order, whenever the vector layer is
    /// missing, down, slow or wrong (invariant 8). As in the hybrid search, a
    /// layer this process never had is tagged and not counted; one that failed is
    /// a degradation, counted by stage.
    /// </summary>
    private async Task<(IReadOnlyList<Neighbourhood> Neighbourhoods, SimilarityRanking RankedBy)> RerankAsync(
        IReadOnlyList<Neighbourhood> neighbourhoods, string culture, Activity? activity, CancellationToken ct)
    {
        // One candidate has no order to change: the vector layer is not asked.
        if (!neighbourhoods.Any(neighbourhood => neighbourhood.Candidates.Count > 1))
            return (neighbourhoods, SimilarityRanking.Rule);

        try
        {
            using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
            budget.CancelAfter(options.Value.VectorBudget);

            var reordered = new List<Neighbourhood>(neighbourhoods.Count);
            foreach (var neighbourhood in neighbourhoods)
            {
                if (neighbourhood.Candidates.Count < 2)
                {
                    reordered.Add(neighbourhood);
                    continue;
                }

                var scores = await similarity.SimilarityAsync(
                    neighbourhood.Viewed.ProductId,
                    [.. neighbourhood.Candidates.Select(candidate => candidate.Product.ProductId)],
                    culture,
                    budget.Token);

                reordered.Add(SimilarProducts.Reorder(neighbourhood, scores));
            }

            return (reordered, SimilarityRanking.Embeddings);
        }
        catch (SearchStageNotConfiguredException missing)
        {
            activity?.SetTag(SearchDegradation.NotConfiguredTag, missing.Stage);
            return (neighbourhoods, SimilarityRanking.Rule);
        }
        catch (Exception exception) when (!ct.IsCancellationRequested)
        {
            SearchDegradation.Record(activity, SearchDegradation.VectorStage, exception, logger);
            return (neighbourhoods, SimilarityRanking.Rule);
        }
    }
}
