using System.Diagnostics;
using Carter;
using ElGuerre.Tendero.Pricing.Domain;
using ElGuerre.Tendero.Pricing.Ports;
using ElGuerre.Tendero.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace ElGuerre.Tendero.Pricing.Features.ListProductDeals;

/// <summary>The promotions one SKU is in, in the order the till weighs them; none when it is in none.</summary>
public sealed record ProductDealsResult(IReadOnlyList<ProductDeal> Items);

public sealed record ListProductDealsQuery(string Sku, string Culture) : IQuery<ProductDealsResult>;

/// <summary>
/// The promotions a product is in, for its page (phase 13c's follow-ups). A card
/// says it already, because the search asks Pricing when it draws one; the page
/// reads the catalogue, which may not reference Pricing, so it asks here, by the
/// SKU on screen. The branch is the one the till prices with
/// (<see cref="IPricedItemReader"/>), and the rule is the card's and the till's
/// (<see cref="ProductDeals"/>), so the page, the card and the basket cannot
/// disagree.
/// </summary>
public sealed class ListProductDealsEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        // GET /api/pricing/deals/{sku}?culture=es
        app.MapGet("/api/pricing/deals/{sku}",
            async Task<Ok<ProductDealsResult>> (
                   string sku, string? culture, HttpContext http,
                   IQueryDispatcher dispatcher, CancellationToken ct) =>
            {
                var resolved = CultureNegotiation.Resolve(culture, http.Request.Headers.AcceptLanguage);

                var result = await dispatcher.SendAsync(new ListProductDealsQuery(sku, resolved), ct);

                http.Response.Headers.ContentLanguage = resolved;
                http.Response.Headers.Vary = "Accept-Language";

                return TypedResults.Ok(result);
            })
            // What the shop takes off is public, as its window is (ListOffers).
            .AllowAnonymous()
            .WithTags("Pricing")
            .WithName("ListProductDeals");
    }
}

public sealed class ListProductDealsHandler(IPricedItemReader items, IPromotionReader promotions, TimeProvider clock)
    : IQueryHandler<ListProductDealsQuery, ProductDealsResult>
{
    private static readonly ActivitySource Telemetry = new(TelemetrySources.Pricing);

    public async Task<ProductDealsResult> HandleAsync(ListProductDealsQuery query, CancellationToken cancellationToken)
    {
        using var activity = Telemetry.StartActivity("pricing.list_product_deals");
        activity?.SetTag("pricing.sku", query.Sku);

        // Only what the shop sells has a branch here: a draft or an unknown SKU is
        // in no promotion, which is an answer and not an error.
        var item = (await items.BySkusAsync([query.Sku], cancellationToken)).FirstOrDefault();
        if (item is null)
            return new ProductDealsResult([]);

        var deals = ProductDeals.Reaching(await promotions.AllAsync(cancellationToken), clock.GetUtcNow(), item.Sku, item.CategoryPath)
            .Select(promotion => new ProductDeal(promotion.Code, promotion.Name.In(query.Culture), promotion.Terms))
            .ToArray();

        activity?.SetTag("pricing.deal_count", deals.Length);
        return new ProductDealsResult(deals);
    }
}
