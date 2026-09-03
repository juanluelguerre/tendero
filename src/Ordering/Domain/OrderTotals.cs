using ElGuerre.Tendero.SharedKernel;

namespace ElGuerre.Tendero.Ordering.Domain;

/// <summary>
/// A discount as the order remembers it: a code, the words the shopper read, and
/// an amount.
///
/// **Strings, never a reference to a `Promotion`.** That is ADR 0002 applied for
/// the third time — after the product name on a line and the variant label — and
/// it is the reason `Ordering` can be told what a discount was without knowing
/// that `Pricing` exists. Renaming a promotion next month must not rewrite what
/// last month's invoice said.
/// </summary>
public sealed record OrderDiscount(string PromotionCode, string Label, Money Amount);

/// <summary>
/// One tax rate and what it collected. Grouped per rate rather than per line,
/// because that is how an invoice prints it and how a tax authority checks it.
/// </summary>
public sealed record OrderTax(string TaxClass, decimal Rate, Money Base, Money Amount);

/// <summary>
/// The shipping the buyer chose, frozen. The carrier's own label at the time,
/// not a live lookup: a rate table edited in March must not change what a
/// January order says it paid for delivery.
/// </summary>
public sealed record OrderShipping(string OptionCode, string Label, Money Amount, int? EstimatedDays);

/// <summary>
/// What the order cost, decided once.
///
/// Every figure here comes from a `PriceQuote` that was revalidated at checkout
/// (ADR 0016), and none of it is ever recomputed. `Total` is not the sum of the
/// lines — it is the sum the buyer agreed to, after discounts, shipping and tax
/// — and an order whose total drifted from what was authorised is a chargeback,
/// not a rounding difference.
/// </summary>
public sealed record OrderTotals(
    Money Subtotal,
    Money DiscountTotal,
    Money Shipping,
    Money TaxTotal,
    Money Total)
{
    public static OrderTotals Zero(string currency) => new(
        Money.Zero(currency), Money.Zero(currency), Money.Zero(currency),
        Money.Zero(currency), Money.Zero(currency));
}

/// <summary>
/// The quote this order was closed against.
///
/// Both halves are kept. The id is how a support conversation finds what was
/// promised; the hash is what made accepting it safe, and storing it means the
/// question "did we honour a stale price?" has an answer months later rather
/// than an opinion.
/// </summary>
public sealed record OrderQuote(string QuoteId, string InputHash, DateTimeOffset IssuedAt);

/// <summary>
/// What the payment provider gave back.
///
/// `Provider` is the keyed adapter's name and travels with the reference because
/// a reference is only meaningful to the system that minted it: refunding a
/// `fake` authorisation through `stripe-mock` is the class of mistake that only
/// shows up in production.
/// </summary>
public sealed record OrderPayment(string Provider, string AuthorizationId, string? CaptureId)
{
    public OrderPayment Captured(string captureId) => this with { CaptureId = captureId };
}
