using System.Diagnostics;
using Carter;
using ElGuerre.Tendero.Catalog.Domain;
using ElGuerre.Tendero.Search.Contracts;
using ElGuerre.Tendero.SharedKernel;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace ElGuerre.Tendero.Search.Features.GetProductCards;

/// <param name="Items">
/// One card per product that can still be shown, in the order asked — ids first,
/// then codes. A product that is no longer Active is left out rather than
/// answered as an error: a best seller withdrawn since it sold, a trail entry for
/// something the shop no longer carries.
/// </param>
public sealed record ProductCardsResult(IReadOnlyList<SearchHit> Items);

public sealed record GetProductCardsQuery(IReadOnlyList<string> Ids, IReadOnlyList<string> Codes, string Culture)
    : IQuery<ProductCardsResult>;

public sealed class GetProductCardsValidator : AbstractValidator<GetProductCardsQuery>
{
    /// <summary>Two rows of the home; a listing is what `/api/search` is for.</summary>
    public const int MaxCards = 24;

    public GetProductCardsValidator()
    {
        RuleFor(query => query.Ids.Count + query.Codes.Count)
            .InclusiveBetween(1, MaxCards)
            .WithMessage($"Ask for 1 to {MaxCards} products, by ids or codes.")
            .OverridePropertyName("ids");

        RuleForEach(query => query.Ids).Must(id => Guid.TryParse(id, out _))
            .WithMessage("A product id is a GUID, as Ordering's rows answer it.")
            .OverridePropertyName("ids");

        RuleForEach(query => query.Codes)
            .Must(code => code is not null && ProductCode.IsWellFormed(ProductCode.Normalise(code)))
            .WithMessage($"A product code is {ProductCode.Length} letters and digits.")
            .OverridePropertyName("codes");

        RuleFor(query => query.Culture).Must(culture => culture is "es" or "en").WithMessage("Supported cultures: es, en.");
    }
}

/// <summary>
/// The cards for products a page already names (R-8): the best sellers and the
/// risers Ordering ranks by id, the trail the browser keeps by code. A card is
/// the index's — name, price, image and stock as a search result shows them —
/// so a row on the home can never disagree with the listing it links to.
/// </summary>
public sealed class GetProductCardsEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        // GET /api/search/cards?ids=0199…&ids=0199…&culture=es
        // GET /api/search/cards?codes=B073WXYZ01&codes=B07C0FFEE1
        app.MapGet("/api/search/cards",
            async Task<Ok<ProductCardsResult>> (
                   string[]? ids, string[]? codes, string? culture,
                   HttpContext http, IQueryDispatcher dispatcher, CancellationToken ct) =>
            {
                var resolved = CultureNegotiation.Resolve(culture, http.Request.Headers.AcceptLanguage);

                var result = await dispatcher.SendAsync(new GetProductCardsQuery(ids ?? [], codes ?? [], resolved), ct);

                http.Response.Headers.ContentLanguage = resolved;
                http.Response.Headers.Vary = "Accept-Language";

                return TypedResults.Ok(result);
            })
            // What search shows anybody, by key instead of by words.
            .AllowAnonymous()
            .WithTags("Search")
            .WithName("GetProductCards");
    }
}

public sealed class GetProductCardsHandler(IProductProfileReader profiles)
    : IQueryHandler<GetProductCardsQuery, ProductCardsResult>
{
    private static readonly ActivitySource Telemetry = new(TelemetrySources.Search);

    public async Task<ProductCardsResult> HandleAsync(GetProductCardsQuery query, CancellationToken ct)
    {
        using var activity = Telemetry.StartActivity("search.cards");
        activity?.SetTag("search.culture", query.Culture);

        var ids = query.Ids.Select(id => Guid.Parse(id).ToString()).Distinct(StringComparer.Ordinal).ToList();
        var codes = query.Codes.Select(ProductCode.Normalise).Distinct(StringComparer.Ordinal).ToList();

        var byId = (await profiles.ByIdsAsync(ids, query.Culture, ct)).ToDictionary(profile => profile.ProductId, StringComparer.Ordinal);
        var byCode = (await profiles.ByCodesAsync(codes, query.Culture, ct)).ToDictionary(profile => profile.Code, StringComparer.Ordinal);

        var cards = ids.Select(id => byId.GetValueOrDefault(id))
            .Concat(codes.Select(code => byCode.GetValueOrDefault(code)))
            .OfType<ProductProfile>()
            .DistinctBy(profile => profile.ProductId, StringComparer.Ordinal)
            .Select(profile => profile.Card)
            .ToList();

        activity?.SetTag("search.cards.found", cards.Count);
        return new ProductCardsResult(cards);
    }
}
