using System.Diagnostics;
using Carter;
using ElGuerre.Tendero.Pricing.Domain;
using ElGuerre.Tendero.Pricing.Ports;
using ElGuerre.Tendero.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace ElGuerre.Tendero.Pricing.Features.ListPromotions;

/// <summary>
/// Every promotion the shop knows, with its combination policy in plain sight.
///
/// The combination column is the reason this screen exists. A promotions list
/// that shows code, dates and amount is a list of discounts; the question a
/// shopkeeper actually has is "if I add this one, what stops applying?", and
/// that is answered by the policy and the exclusivity group, not by the amount.
///
/// Labels come back in EVERY culture, like the attribute definitions screen and
/// for the same reason: a resolved string would hide the missing translation
/// that is half of what the screen is for.
/// </summary>
public sealed record ListPromotionsQuery : IQuery<ListPromotionsResult>;

public sealed record PromotionView(
    string Code,
    IReadOnlyDictionary<string, string> Name,
    string Effect,
    string EffectDetail,
    string Combination,
    int Priority,
    string? ExclusivityGroup,
    string? Segment,
    string? CouponCode,
    DateTimeOffset? ValidFrom,
    DateTimeOffset? ValidTo,
    bool IsActive,
    IReadOnlyList<string> MissingCultures);

public sealed record ListPromotionsResult(IReadOnlyList<PromotionView> Items);

public sealed class ListPromotionsEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/pricing/promotions",
            async Task<Ok<ListPromotionsResult>> (
                   IQueryDispatcher dispatcher, CancellationToken ct) =>
                TypedResults.Ok(await dispatcher.SendAsync(new ListPromotionsQuery(), ct)))
            // Shopkeeper only. A promotion that is not running yet, or one that
            // needs a coupon, is commercial information: listing it publicly
            // would hand out every coupon code the shop has.
            .RequireAuthorization(TenderoPolicyNames.Shopkeeper)
            .WithTags("Pricing")
            .WithName("ListPromotions");
    }
}

public sealed class ListPromotionsHandler(IPromotionReader promotions, TimeProvider clock)
    : IQueryHandler<ListPromotionsQuery, ListPromotionsResult>
{
    private static readonly ActivitySource Telemetry = new(TelemetrySources.Pricing);

    public async Task<ListPromotionsResult> HandleAsync(
        ListPromotionsQuery query, CancellationToken cancellationToken)
    {
        using var activity = Telemetry.StartActivity("pricing.list_promotions");

        var all = await promotions.AllAsync(cancellationToken);
        var now = clock.GetUtcNow();

        activity?.SetTag("pricing.promotion_count", all.Count);

        return new ListPromotionsResult(
        [
            .. all
                .OrderBy(promotion => promotion.Priority)
                .ThenBy(promotion => promotion.Code, StringComparer.Ordinal)
                .Select(promotion => View(promotion, now))
        ]);
    }

    private static PromotionView View(Promotion promotion, DateTimeOffset now) => new(
        promotion.Code,
        promotion.Name.Values,
        promotion.Effect.Kind,
        Detail(promotion.Effect),
        promotion.Combination.ToString(),
        promotion.Priority,
        promotion.ExclusivityGroup,
        promotion.Segment,
        promotion.CouponCode,
        promotion.ValidFrom,
        promotion.ValidTo,
        promotion.IsActiveAt(now),
        [.. CultureNegotiation.Supported.Where(culture => !promotion.Name.Cultures.Contains(culture))]);

    /// <summary>
    /// The effect as a short technical string. Not localized on purpose: this is
    /// the shopkeeper's own view of the rule, and "20%" reads the same in both
    /// languages. What a shopper sees is the promotion's name, which is.
    /// </summary>
    private static string Detail(PromotionEffect effect) => effect switch
    {
        PercentOffLine(var percent) => $"{percent}%",
        AmountOffOrder(var amount) => amount.ToString(),
        BuyXGetY(var buy, var free) => $"{buy}+{free}",
        FreeShipping => "-",
        _ => effect.Kind
    };
}
