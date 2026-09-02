using ElGuerre.Tendero.SharedKernel;

namespace ElGuerre.Tendero.Pricing.Domain;

/// <summary>
/// A line to be priced, carrying EVERYTHING the engine needs to know about the
/// catalogue.
///
/// The catalogue facts — list price, category branch, tax class — travel as
/// values rather than as a reference to <c>Variant</c>. That is why Pricing does
/// not depend on Catalog, and therefore why the engine can be verified with
/// properties without standing up a database.
///
/// That a port supplies them rather than the caller matters too: if the request
/// body declared its own category, anybody would claim the KITCHEN discount on a
/// backpack.
/// </summary>
public sealed record CartItem(
    VariantId VariantId,
    string Sku,
    int Quantity,
    Money CatalogPrice,
    string? CategoryPath = null,
    string? TaxClass = null);

/// <summary>
/// A line already priced. <c>Discount</c> accumulates what every promotion that
/// reached it took off, and <c>Net</c> never drops below zero: a discount that
/// left a line negative would turn the cart into an income.
/// </summary>
public sealed record PricedLine(
    VariantId VariantId,
    string Sku,
    int Quantity,
    Money UnitPrice,
    string PriceSource,
    string? CategoryPath,
    string? TaxClass,
    Money Discount)
{
    public Money Gross => UnitPrice * Quantity;

    public Money Net => Gross - Discount;

    public PricedLine Discounted(Money extra) =>
        this with { Discount = Discount + Min(extra, Net) };

    private static Money Min(Money a, Money b) => a < b ? a : b;
}

/// <summary>
/// The cart while it is being priced: lines, shipping and the currency that
/// binds them. A value, replaced at each step rather than mutated, which is what
/// lets promotion evaluation be a function.
/// </summary>
public sealed record PricedCart(
    string Currency,
    string Segment,
    IReadOnlyList<PricedLine> Lines,
    Money Shipping)
{
    public Money Gross => Sum(Lines.Select(line => line.Gross));

    public Money DiscountTotal => Sum(Lines.Select(line => line.Discount));

    /// <summary>The taxable base of the lines: gross less discounts.</summary>
    public Money Net => Gross - DiscountTotal;

    public PricedCart WithLines(IReadOnlyList<PricedLine> lines) => this with { Lines = lines };

    private Money Sum(IEnumerable<Money> amounts) =>
        amounts.Aggregate(Money.Zero(Currency), (total, amount) => total + amount);
}

/// <summary>
/// What happened to a promotion. Applied or suppressed — and suppressed ALWAYS
/// with its reason, which is the whole point of the phase.
/// </summary>
public enum DiscountOutcome { Applied, Suppressed }

/// <summary>
/// An evaluated promotion. The suppressed ones come back too: the interface
/// shows them struck through with their reason, and a test can assert that the
/// rule which fired was the right one.
/// </summary>
public sealed record AppliedDiscount(
    string PromotionCode,
    LocalizedText Label,
    Money Amount,
    string EffectKind,
    CombinationPolicy Combination,
    DiscountOutcome Outcome,
    RuleReason? Reason)
{
    public static AppliedDiscount Applied(Promotion promotion, Money amount) =>
        new(promotion.Code, promotion.Name, amount, promotion.Effect.Kind,
            promotion.Combination, DiscountOutcome.Applied, Reason: null);

    /// <summary>A suppressed promotion is worth zero — but zero IN THE CART'S
    /// CURRENCY, because summing the whole list must not blow up over a currency
    /// invented while building the case that did not apply.</summary>
    public static AppliedDiscount Suppressed(
        Promotion promotion, RuleReason reason, string currency) =>
        new(promotion.Code, promotion.Name, Money.Zero(currency),
            promotion.Effect.Kind, promotion.Combination, DiscountOutcome.Suppressed, reason);
}
