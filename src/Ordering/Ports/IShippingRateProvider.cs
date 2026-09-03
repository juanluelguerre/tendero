using ElGuerre.Tendero.SharedKernel;

namespace ElGuerre.Tendero.Ordering.Ports;

/// <summary>
/// One thing to move: a SKU and how many. No weight, and that absence is
/// deliberate — see <see cref="IShippingRateProvider"/>.
/// </summary>
public sealed record ShippableLine(string Sku, int Quantity);

/// <summary>
/// What a rate is a function of.
///
/// The destination is here rather than in `Pricing` because that is exactly why
/// shipping lives in `Ordering`: a rate needs an address, and dragging `Address`
/// into `Pricing` would ruin the purity that makes the promotion engine
/// property-testable. The subtotal travels too, so "free over 50 €" is a rate
/// rule rather than a promotion pretending to be one.
/// </summary>
public sealed record ShippingQuoteRequest(
    Address Destination,
    IReadOnlyList<ShippableLine> Lines,
    Money Subtotal)
{
    public int ItemCount => Lines.Sum(line => line.Quantity);
}

/// <summary>
/// A way to get the parcel there, and what it costs.
///
/// The label is <see cref="LocalizedText"/> because it is user-facing and
/// invariant 6 has no exceptions — "Standard delivery" and "Envío estándar" are
/// the same option. The order freezes the resolved string, not this.
/// </summary>
public sealed record ShippingOption(
    string Code,
    LocalizedText Label,
    Money Amount,
    int? EstimatedDays);

/// <summary>
/// How much it costs to send this there.
///
/// Keyed adapters, one contract suite, and a new carrier is a new class plus a
/// registration — ADR 0003 doing the job it was designed for.
///
/// **Neither adapter reads a weight**, and the roadmap asked for one that did.
/// The catalogue carries no weight: adding one means a new attribute definition
/// on six products, which changes `attributesText`, which moves a measured
/// search baseline — for a reason that has nothing to do with search. The two
/// adapters differ in the input they read instead, which is a better test of the
/// port anyway: `flat-rate` ignores the destination entirely and `zone-rate` is
/// a function of it, so a contract that both satisfy is a contract about
/// shipping rather than about arithmetic.
/// </summary>
public interface IShippingRateProvider
{
    /// <summary>Stable, lowercase, kebab. It is what the keyed registration and
    /// the configuration both use.</summary>
    string Key { get; }

    /// <summary>
    /// The options for this destination, cheapest first.
    ///
    /// **Empty means "we do not ship there"**, and it is a legitimate answer
    /// rather than an error: a country outside the zone table is a business fact,
    /// and turning it into an exception would make checkout show a stack trace
    /// where it should show a sentence.
    /// </summary>
    Task<IReadOnlyList<ShippingOption>> QuoteAsync(
        ShippingQuoteRequest request, CancellationToken cancellationToken = default);
}

/// <summary>
/// The configured providers by key, so a slice can ask for one by name without
/// knowing which exist. Same shape as the tax calculators and the allocation
/// strategies.
/// </summary>
public interface IShippingRateProviderRegistry
{
    IReadOnlyCollection<string> Keys { get; }

    IShippingRateProvider Get(string key);
}
