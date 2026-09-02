using ElGuerre.Tendero.Pricing.Domain;
using ElGuerre.Tendero.SharedKernel;

namespace ElGuerre.Tendero.Pricing.Ports;

/// <summary>What gets asked to know the price of one unit.</summary>
public sealed record PriceRequest(string Sku, Money CatalogPrice, string Segment, DateTimeOffset At);

/// <summary>
/// The price and where it came from. Provenance is not decoration: without it,
/// the backoffice cannot answer "why does this customer see 22,41 €?" without
/// replaying the resolution by hand.
/// </summary>
public sealed record ResolvedPrice(Money Unit, string Source)
{
    /// <summary>When no list carries the SKU, the catalogue price rules. That
    /// this value exists is what lets a freshly imported product be priced
    /// without touching any tariff.</summary>
    public const string Catalog = "catalog";
}

/// <summary>
/// Resolves the price of one unit against the tariffs.
///
/// **It is a pure function**: the price book arrives as an argument instead of
/// being fetched inside. That looks like a detour and is the opposite — it is
/// what stops a five-line cart from making five queries, and what lets
/// properties be written about the result without standing anything up.
///
/// A port and not a static method because price policy is exactly where a real
/// shop ends up with a second implementation — negotiated prices, volume prices
/// — and ADR 0003 already says how that changes: another class and one line of
/// registration.
/// </summary>
public interface IPriceResolver
{
    ResolvedPrice Resolve(PriceBook book, PriceRequest request);
}
