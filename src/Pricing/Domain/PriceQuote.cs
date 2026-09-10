using ElGuerre.Tendero.SharedKernel;

namespace ElGuerre.Tendero.Pricing.Domain;

/// <summary>
/// A line exactly as it was quoted, with the provenance of its price in plain
/// sight.
/// </summary>
public sealed record QuotedLine(
    VariantId VariantId,
    string Sku,
    int Quantity,
    Money UnitPrice,
    string PriceSource,
    Money Discount,
    Money Net);

/// <summary>
/// A closed price for a cart, with an expiry and a fingerprint of its inputs.
///
/// It exists because a price is QUOTED live and FROZEN at purchase (ADR 0016),
/// and anything can happen between those two moments: a promotion expires, a
/// tariff changes, somebody adds a line. The fingerprint is what turns "anything
/// can happen" into a one-line check at checkout.
///
/// The fingerprint covers TWO things, and the second is the one that gets
/// forgotten: what the shopper asked for, and the data they were answered with.
/// Without that second half, changing a price in the backoffice would leave live
/// quotes promising an amount nobody honours any more.
///
/// The same mechanism pays for itself twice more later: revalidating a quote
/// against its fingerprint and verifying that an AP2 mandate covers an amount
/// are the same piece — something was frozen, and it has to still hold.
/// </summary>
public sealed record PriceQuote(
    string QuoteId,
    string InputHash,
    DateTimeOffset IssuedAt,
    DateTimeOffset ExpiresAt,
    string Currency,
    string Segment,
    IReadOnlyList<QuotedLine> Lines,
    IReadOnlyList<AppliedDiscount> Discounts,
    IReadOnlyList<TaxLineView> Taxes,
    Money Subtotal,
    Money DiscountTotal,
    Money Shipping,
    Money TaxTotal,
    Money Total)
{
    /// <summary>
    /// How long a quote lives. Short on purpose: it is the window in which the
    /// shop commits to a price, and committing for an hour against a catalogue
    /// that changes is promising what cannot be kept.
    /// </summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(15);

    public bool HasExpiredAt(DateTimeOffset at) => at >= ExpiresAt;

    /// <summary>
    /// Whether this quote still holds for a set of inputs. It returns why it
    /// failed rather than a boolean: "it expired" and "the cart changed" are
    /// told to a shopper in different words.
    ///
    /// It was written as the check <c>PlaceOrder</c> would make in phase 5, and
    /// checkout went another way: it re-runs the engine and compares the
    /// fingerprints (ADR 0025). Only the tests call this today.
    /// </summary>
    public QuoteValidity ValidateAt(DateTimeOffset at, string inputHash) =>
        HasExpiredAt(at) ? QuoteValidity.Expired
        : !string.Equals(InputHash, inputHash, StringComparison.Ordinal) ? QuoteValidity.InputsChanged
        : QuoteValidity.Valid;
}

public enum QuoteValidity { Valid, Expired, InputsChanged }

/// <summary>The tax breakdown as it is shown. A view and not the port's
/// <c>TaxLine</c>, because a quote is a frozen value and should not drag along
/// the shape of the calculator that produced it.</summary>
public sealed record TaxLineView(string TaxClass, decimal Rate, Money Base, Money Amount);
