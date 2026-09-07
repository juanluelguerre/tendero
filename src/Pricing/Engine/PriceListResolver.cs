using ElGuerre.Tendero.Pricing.Domain;
using ElGuerre.Tendero.Pricing.Ports;

namespace ElGuerre.Tendero.Pricing.Engine;

/// <summary>
/// The first applicable list that knows the SKU rules; if none knows it, the
/// catalogue price does.
///
/// The fallback is not a detail: without it, importing a new product would leave
/// it priceless until somebody added it to a tariff, and a shop with unbuyable
/// articles is worse than a shop with no tariffs.
/// </summary>
public sealed class PriceListResolver : IPriceResolver
{
    public ResolvedPrice Resolve(PriceBook book, PriceRequest request)
    {
        foreach (var list in book.ApplicableTo(request.Segment, request.At))
        {
            if (list.PriceFor(request.Sku) is not { } price)
                continue;

            // A tariff in a currency other than the catalogue's is not a better
            // price, it is incoherent data. Skipped rather than mixed:
            // multi-currency is deferred on purpose, and a cart with two
            // currencies does not add up.
            if (!String.Equals(price.Currency, request.CatalogPrice.Currency, StringComparison.OrdinalIgnoreCase))
                continue;

            return new ResolvedPrice(price, list.Code);
        }

        return new ResolvedPrice(request.CatalogPrice, ResolvedPrice.Catalog);
    }
}
