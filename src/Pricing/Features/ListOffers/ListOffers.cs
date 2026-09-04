using System.Diagnostics;
using Carter;
using ElGuerre.Tendero.Pricing.Domain;
using ElGuerre.Tendero.Pricing.Ports;
using ElGuerre.Tendero.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace ElGuerre.Tendero.Pricing.Features.ListOffers;

/// <summary>
/// What the shop is offering, as a shopper may see it.
///
/// **The same table already has a reader, and it is not this one.**
/// `/api/pricing/promotions` is behind the shopkeeper policy and stays there,
/// because it answers with the coupon codes, the segments, the priorities and
/// the combination rules — the shop's own machinery. A shopper reading that
/// would learn which codes exist without having been given one, and which
/// segment gets the better price.
///
/// So this is a second projection of the same rows, and it is defined by what it
/// LEAVES OUT. It is the same call `GET /api/orders/mine` made against the
/// shopkeeper's order list: two audiences, two slices, and the narrow one never
/// grows a parameter that would widen it.
/// </summary>
public sealed record OfferView(
    string Code,

    /// <summary>Resolved in the reader's culture. The shopkeeper's view returns
    /// every culture; a shopper gets theirs.</summary>
    string Name,

    /// <summary>
    /// `percent-off-line`, `amount-off-order`, `buy-x-get-y` or `free-shipping`
    /// — enough for the interface to choose a phrasing and an icon, and not
    /// enough to reconstruct the rule.
    ///
    /// The exact strings are written out because a client switches on them, and
    /// the first version of that switch guessed PascalCase and silently fell
    /// through to the default for every offer.
    /// </summary>
    string Kind,

    /// <summary>
    /// The headline figure: `20` for a percentage, `10.00` for an amount off,
    /// `3` for buy-three-get-one, null for free shipping. A number rather than a
    /// sentence, because the sentence is the interface's job and it has to exist
    /// in two languages.
    /// </summary>
    decimal? Amount,

    /// <summary>When it stops, when it does. A shopper can act on a deadline;
    /// they cannot act on a start date that has already passed.</summary>
    DateTimeOffset? Until);

public sealed record ListOffersResult(IReadOnlyList<OfferView> Items);

public sealed record ListOffersQuery(string Culture) : IQuery<ListOffersResult>;

public sealed class ListOffersEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        // GET /api/pricing/offers?culture=es
        app.MapGet("/api/pricing/offers",
            async Task<Ok<ListOffersResult>> (
                   string? culture, HttpContext http,
                   IQueryDispatcher dispatcher, CancellationToken ct) =>
            {
                var resolved = CultureNegotiation.Resolve(
                    culture, http.Request.Headers.AcceptLanguage);

                var result = await dispatcher.SendAsync(new ListOffersQuery(resolved), ct);

                http.Response.Headers.ContentLanguage = resolved;
                http.Response.Headers.Vary = "Accept-Language";

                return TypedResults.Ok(result);
            })
            // What a shop puts in its window is public by definition.
            .AllowAnonymous()
            .WithTags("Pricing")
            .WithName("ListOffers");
    }
}

public sealed class ListOffersHandler(IPromotionReader promotions, TimeProvider clock)
    : IQueryHandler<ListOffersQuery, ListOffersResult>
{
    private static readonly ActivitySource Telemetry = new(TelemetrySources.Pricing);

    public async Task<ListOffersResult> HandleAsync(
        ListOffersQuery query, CancellationToken cancellationToken)
    {
        using var activity = Telemetry.StartActivity("pricing.list_offers");

        var all = await promotions.AllAsync(cancellationToken);
        var now = clock.GetUtcNow();

        var offers = all
            .Where(promotion => Public(promotion, now))
            .OrderBy(promotion => promotion.Priority)
            .ThenBy(promotion => promotion.Code, StringComparer.Ordinal)
            .Select(promotion => View(promotion, query.Culture))
            .ToArray();

        activity?.SetTag("pricing.offer_count", offers.Length);
        activity?.SetTag("pricing.promotion_count", all.Count);

        return new ListOffersResult(offers);
    }

    /// <summary>
    /// **An offer worth advertising is one anybody reading it can actually get,
    /// right now.** Three conditions, and each one is a promise the window would
    /// otherwise break:
    ///
    /// - it is inside its validity window, or the shop is advertising something
    ///   that will not apply at the till;
    /// - it needs no coupon, because showing a discount whose code the reader
    ///   does not have is a tease rather than an offer;
    /// - it is not restricted to a segment, because a `vip` price shown to
    ///   everyone is the same tease with a worse aftertaste.
    ///
    /// Everything filtered out still exists and still applies to whoever it was
    /// written for — the cart shows it, with its reason, which is where a
    /// suppressed or targeted promotion belongs.
    /// </summary>
    private static bool Public(Promotion promotion, DateTimeOffset now) =>
        promotion.IsActiveAt(now)
        && promotion.CouponCode is null
        && promotion.Segment is null;

    private static OfferView View(Promotion promotion, string culture) => new(
        promotion.Code,
        promotion.Name.In(culture),
        promotion.Effect.Kind,
        Headline(promotion.Effect),
        promotion.ValidTo);

    /// <summary>The figure, without the sentence around it. The sentence has to
    /// exist in es and en, so it belongs to the interface.</summary>
    private static decimal? Headline(PromotionEffect effect) => effect switch
    {
        PercentOffLine(var percent) => percent,
        AmountOffOrder(var amount) => amount.Amount,
        BuyXGetY(var buy, _) => buy,
        _ => null,
    };
}
