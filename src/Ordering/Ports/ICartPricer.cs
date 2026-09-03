using ElGuerre.Tendero.Ordering.Domain;
using ElGuerre.Tendero.SharedKernel;

namespace ElGuerre.Tendero.Ordering.Ports;

/// <summary>A line as pricing answered it: what it costs each, and how many.</summary>
public sealed record PricedCartLine(string Sku, Money UnitPrice, int Quantity);

public enum PricingOutcome { Quoted, UnknownSkus, MixedCurrencies }

/// <summary>
/// A closed price for a cart, in `Ordering`'s own vocabulary.
///
/// Every field here is either a SharedKernel value or a type `Ordering` owns.
/// That translation is the whole reason the port exists: `Pricing`'s
/// `PriceQuote` lives in `Pricing.Domain`, and letting it into a handler would
/// make two contexts share a type — the crossing ADR 0014 exists to prevent, and
/// the same discipline that keeps `Ordering` to `Inventory`'s ports.
/// </summary>
public sealed record CartPricing(
    PricingOutcome Outcome,
    string QuoteId,
    string InputHash,
    DateTimeOffset IssuedAt,
    DateTimeOffset ExpiresAt,
    string Currency,
    OrderTotals Totals,
    IReadOnlyList<PricedCartLine> Lines,
    IReadOnlyList<OrderDiscount> Discounts,
    IReadOnlyList<OrderTax> Taxes,
    IReadOnlyList<string> Offending)
{
    public OrderQuote AsOrderQuote() => new(QuoteId, InputHash, IssuedAt);
}

/// <summary>
/// What a cart costs right now.
///
/// **Checkout re-runs the same engine the storefront saw.** That is not
/// convenience: ADR 0016's fingerprint check is only meaningful if the number it
/// is checked against was produced the same way. A checkout that recomputed the
/// price with its own arithmetic would compare two hashes from two different
/// systems and call the agreement proof.
///
/// The shipping amount goes IN as an input, because `FreeShipping` is a
/// promotion effect and cannot be evaluated without it. That is the loop the
/// roadmap describes — shipping is priced in `Ordering`, fed back into
/// `Pricing`, and both directions are values, so nothing circular happens at the
/// type level.
/// </summary>
public interface ICartPricer
{
    Task<CartPricing> QuoteAsync(
        IReadOnlyList<(string Sku, int Quantity)> lines,
        string culture,
        Money shipping,
        IReadOnlyList<string> coupons,
        CancellationToken cancellationToken = default);
}
