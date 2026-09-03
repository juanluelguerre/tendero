using ElGuerre.Tendero.Ordering.Domain;
using ElGuerre.Tendero.Ordering.Ports;
using ElGuerre.Tendero.Pricing.Domain;
using ElGuerre.Tendero.Pricing.Features.QuoteCart;
using ElGuerre.Tendero.SharedKernel;

namespace ElGuerre.Tendero.Ordering.Adapters;

/// <summary>
/// The one file in `Ordering` that is allowed to know `Pricing` exists.
///
/// An architecture rule enforces exactly that, in the shape the Elasticsearch
/// client already has here: the dependency is legitimate, and it is confined to
/// a single named adapter so it cannot spread into handlers. Everything on the
/// other side of this class speaks in `Ordering`'s own values.
///
/// It goes through <c>IQueryDispatcher</c> rather than composing
/// <c>IPriceResolver</c> and <c>IPromotionEngine</c> itself, and that is the
/// point rather than laziness: checkout revalidates a fingerprint, and a
/// fingerprint is only evidence if both sides computed it the same way. A second
/// assembly of the pricing pipeline would be a second engine, and the day the
/// two drifted the check would still pass.
/// </summary>
internal sealed class PricingCartPricer(IQueryDispatcher dispatcher) : ICartPricer
{
    public async Task<CartPricing> QuoteAsync(
        IReadOnlyList<(string Sku, int Quantity)> lines,
        string culture,
        Money shipping,
        IReadOnlyList<string> coupons,
        CancellationToken cancellationToken = default)
    {
        var result = await dispatcher.SendAsync(
            new QuoteCartQuery(
                culture,
                // The segment is deliberately NOT passed: `QuoteCart` resolves it
                // from the principal, so a guest asking for `vip` is quoted
                // `retail`. Forwarding it from here would be a back door into
                // the tariff.
                Segment: null,
                [.. lines.Select(line => new QuoteLineInput(line.Sku, line.Quantity))],
                shipping.Amount,
                coupons),
            cancellationToken);

        if (result.Outcome != QuoteOutcome.Quoted || result.Quote is null)
            return Failed(
                result.Outcome == QuoteOutcome.UnknownSkus
                    ? PricingOutcome.UnknownSkus
                    : PricingOutcome.MixedCurrencies,
                result.Offending,
                shipping.Currency);

        return Translate(result.Quote, culture);
    }

    /// <summary>
    /// Pricing's value into Ordering's. The labels are resolved HERE, in the
    /// buyer's culture, because what the order freezes is the sentence the buyer
    /// read — the same rule that freezes the product name onto a line.
    ///
    /// Suppressed promotions do not cross. They exist so a cart screen can say
    /// "not combinable with Summer sale"; an order records what was applied, and
    /// a discount of zero on an invoice is a question nobody can answer.
    /// </summary>
    private static CartPricing Translate(PriceQuote quote, string culture) => new(
        PricingOutcome.Quoted,
        quote.QuoteId,
        quote.InputHash,
        quote.IssuedAt,
        quote.ExpiresAt,
        quote.Currency,
        new OrderTotals(
            quote.Subtotal, quote.DiscountTotal, quote.Shipping, quote.TaxTotal, quote.Total),
        [.. quote.Lines.Select(line => new PricedCartLine(line.Sku, line.UnitPrice, line.Quantity))],
        [
            .. quote.Discounts
                .Where(discount => discount.Outcome == DiscountOutcome.Applied)
                .Select(discount => new OrderDiscount(
                    discount.PromotionCode, discount.Label.In(culture), discount.Amount))
        ],
        [
            .. quote.Taxes.Select(tax => new OrderTax(tax.TaxClass, tax.Rate, tax.Base, tax.Amount))
        ],
        []);

    private static CartPricing Failed(
        PricingOutcome outcome, IReadOnlyList<string> offending, string currency) => new(
        outcome,
        string.Empty,
        string.Empty,
        DateTimeOffset.MinValue,
        DateTimeOffset.MinValue,
        currency,
        OrderTotals.Zero(currency),
        [],
        [],
        [],
        offending);
}
