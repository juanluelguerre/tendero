using System.ComponentModel;
using System.Diagnostics.Metrics;
using Carter;
using ElGuerre.Tendero.Search.Contracts;
using ElGuerre.Tendero.SharedKernel;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace ElGuerre.Tendero.Search.Features.SearchProducts;

public sealed record SearchProductsQuery(
    string Q, string Culture, int Page, int PageSize, string? Category = null, ProductSort Sort = ProductSort.Relevance,
    SearchFilters? Filters = null)
    : IQuery<SearchResultPage>;

public sealed class SearchProductsValidator : AbstractValidator<SearchProductsQuery>
{
    public SearchProductsValidator()
    {
        // **Empty is allowed; one character is not.** Nothing typed is a
        // browse — a category page, a home page row — and it is the question
        // every entry point that is not a search box asks. A single character
        // is still a query, and still one that matches so much the answer is
        // noise, which is the rule the storefront has enforced on its own side
        // since the first slice.
        RuleFor(x => x.Q).MinimumLength(2).MaximumLength(200)
            .When(x => !string.IsNullOrEmpty(x.Q));

        RuleFor(x => x.Category).MaximumLength(60);

        // A closed set, because a sort the index cannot honour is a promise
        // broken at run time rather than at the door.
        RuleFor(x => x.Sort).IsInEnum()
            .WithMessage($"Supported sorts: {string.Join(", ", Enum.GetNames<ProductSort>())}.");
        RuleFor(x => x.Culture).Must(c => c is "es" or "en")
            .WithMessage("Supported cultures: es, en.");
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 50);

        RuleFor(x => x.Filters!).SetValidator(new SearchFiltersValidator()).When(x => x.Filters is not null);
    }
}

/// <summary>
/// What a filter may carry. Bounded in count and length because every value
/// becomes a clause of the query, and a URL is something anybody can write.
/// </summary>
public sealed partial class SearchFiltersValidator : AbstractValidator<SearchFilters>
{
    /// <summary>More choices than a column holds in one facet, and far fewer than would make a query heavy.</summary>
    public const int MaxChoices = 30;

    public SearchFiltersValidator()
    {
        RuleFor(x => x.Brands.Count).LessThanOrEqualTo(MaxChoices).OverridePropertyName("brand");
        RuleForEach(x => x.Brands).NotEmpty().MaximumLength(100).OverridePropertyName("brand");

        RuleFor(x => x.Within.Count).LessThanOrEqualTo(MaxChoices).OverridePropertyName("within");
        RuleForEach(x => x.Within).NotEmpty().MaximumLength(60).OverridePropertyName("within");

        // "CODE:VALUE", the code as the catalogue writes it — upper case, digits
        // and underscores — which is also what keeps it from ever meeting one of
        // the built-in facets' lower-case codes.
        RuleFor(x => x.Facets.Count).LessThanOrEqualTo(MaxChoices).OverridePropertyName("f");
        RuleForEach(x => x.Facets).Must(pair => pair is not null && CodeAndValue().IsMatch(pair))
            .WithMessage("A filter is CODE:VALUE, the code in upper case (COLOR:BLACK, SIZE:42, INDUCTION:true).")
            .OverridePropertyName("f");

        RuleFor(x => x.MinPrice).GreaterThanOrEqualTo(0).OverridePropertyName("minPrice");
        RuleFor(x => x.MaxPrice).GreaterThanOrEqualTo(0).OverridePropertyName("maxPrice");
        RuleFor(x => x.MaxPrice).GreaterThanOrEqualTo(x => x.MinPrice!.Value)
            .When(x => x.MinPrice is not null && x.MaxPrice is not null)
            .WithMessage("maxPrice cannot be below minPrice.")
            .OverridePropertyName("maxPrice");

        // A campaign's code as Pricing writes it (TENDERO_DAYS), in any case.
        RuleFor(x => x.Campaign).Matches("^[A-Za-z0-9_]{1,40}$")
            .When(x => x.Campaign is not null)
            .WithMessage("A campaign is its code: letters, digits and underscores (TENDERO_DAYS).")
            .OverridePropertyName("campaign");
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"^[A-Z0-9_]{1,60}:[^\s:]{1,100}$")]
    private static partial System.Text.RegularExpressions.Regex CodeAndValue();
}

public sealed class SearchProductsEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        // GET /api/search?q=zapatillas%20running&culture=es&page=1&pageSize=20
        // GET /api/search?category=KITCHEN            — browse a whole branch
        // GET /api/search                             — browse everything
        // GET /api/search?category=SHOES&brand=Vertex&brand=Caminante&f=SIZE:42&maxPrice=80&inStock=true&sort=PriceAsc
        //                                             — narrowed (R-7.3, F-2): within a filter the
        //                                               choices add up, across filters they narrow
        // GET /api/search?campaign=TENDERO_DAYS       — what a running campaign's promotions reach (R-8)
        app.MapGet("/api/search",
            async Task<Ok<SearchResultPage>> (
                   string? q, string? culture, int? page, int? pageSize, string? category, WireEnum<ProductSort>? sort,
                   string[]? brand, string[]? within, string[]? f,
                   [Description(SearchPrices.InMinorUnits)] long? minPrice, [Description(SearchPrices.InMinorUnits)] long? maxPrice, bool? onOffer, bool? newArrivals, bool? inStock, string? campaign,
                   HttpContext http, IQueryDispatcher dispatcher, CancellationToken ct) =>
            {
                var filters = new SearchFilters
                {
                    Brands = brand ?? [],
                    Within = within ?? [],
                    Facets = f ?? [],
                    // Asked in the unit every price is answered in (ADR 0059):
                    // a facet's end can be sent straight back.
                    MinPrice = SearchPrices.FromMinorUnits(minPrice),
                    MaxPrice = SearchPrices.FromMinorUnits(maxPrice),
                    OnOffer = onOffer ?? false,
                    NewArrivals = newArrivals ?? false,
                    InStock = inStock ?? false,
                    Campaign = string.IsNullOrWhiteSpace(campaign) ? null : campaign.Trim(),
                };

                // Culture: explicit query parameter > Accept-Language > "es"
                // (ADR 0013). The chain lives in CultureNegotiation since the
                // third endpoint that needed it turned up.
                var resolved = CultureNegotiation.Resolve(
                    culture, http.Request.Headers.AcceptLanguage);

                var result = await dispatcher.SendAsync(
                    new SearchProductsQuery(
                        q ?? string.Empty, resolved, page ?? 1, pageSize ?? 20, category, sort?.Value ?? ProductSort.Relevance,
                        filters.IsEmpty ? null : filters), ct);

                // The other half of the negotiation: the client asks, the server
                // declares which language it answered in. Vary because the
                // response DEPENDS on Accept-Language when the parameter is
                // absent, and a shared cache without it serves Spanish to
                // whoever asked for English.
                http.Response.Headers.ContentLanguage = resolved;
                http.Response.Headers.Vary = "Accept-Language";

                return TypedResults.Ok(result);
            })
            .AllowAnonymous()   // searching is what an agent does without identifying itself
            .WithTags("Search")
            .WithName("SearchProducts");
    }
}

public sealed class SearchProductsHandler(
    [FromKeyedServices(SearchStrategies.Hybrid)] ISearchStrategy hybrid,
    ISearchAnswerer answerer,
    IActorAccessor actors)
    : IQueryHandler<SearchProductsQuery, SearchResultPage>
{
    // Hybrid answers every search since 2026-09-17: SearchVector met its removal
    // condition in three gate runs (ADR 0023, amended) and was deleted. Lexical is
    // not gone, it is underneath — what hybrid degrades to, byte for byte, when
    // the vector layer is down, slow or was never started — and CI still gates
    // lexical on its own thresholds. The storefront, the MCP tools and UCP all
    // dispatch this query, so this line is where all of them went hybrid.
    public async Task<SearchResultPage> HandleAsync(SearchProductsQuery query, CancellationToken ct)
    {
        var search = new ProductSearchQuery(
            query.Q, query.Culture, query.Page, query.PageSize, query.Category, query.Sort,
            query.Filters is { IsEmpty: false } filters ? filters : null);

        var page = await hybrid.SearchAsync(search, ct);
        SearchRequests.Record(query, page, actors.Current);

        // What kind of answer it is, once (ADR 0032).
        return page with { Answer = await answerer.AnswerAsync(search, ct) };
    }
}

/// <summary>
/// `tendero.search.requests` (the tracking plan): every search answered, by who
/// asked and through what, whether it was typed or browsed, and whether anything
/// came back. Here and not in the endpoint because the storefront, the MCP tools
/// and UCP all dispatch this query, so this is the one place all three pass.
/// </summary>
internal static class SearchRequests
{
    private static readonly Meter Meter = new(TelemetrySources.Search);

    private static readonly Counter<long> Requests = Meter.CreateCounter<long>(
        "tendero.search.requests", unit: "{search}",
        description: "Searches answered, by who asked, through what, and whether anything came back.");

    public static void Record(SearchProductsQuery query, SearchResultPage page, Actor actor) =>
        Requests.Add(1,
            actor.KindLabel,
            actor.SurfaceLabel,
            // Nothing typed is a browse: a category page, a home-page row.
            new("mode", string.IsNullOrWhiteSpace(query.Q) ? "browse" : "query"),
            new("category", query.Category is not null),
            new("result", page.Hits.Count > 0 ? "found" : "empty"));
}
