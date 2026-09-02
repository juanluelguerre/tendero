using ElGuerre.Tendero.Catalog.Domain;
using ElGuerre.Tendero.Catalog.Ports;
using ElGuerre.Tendero.Pricing.Ports;
using Microsoft.EntityFrameworkCore;

namespace ElGuerre.Tendero.Persistence;

/// <summary>
/// What the catalogue knows about a SKU, for the pricing context.
///
/// This adapter lives here and not in Pricing on purpose. Pricing must not
/// reference Catalog — that is what keeps its engine a function of values and
/// therefore property-testable — but somebody has to bridge the two, and the
/// place where every other outbound adapter already lives is this project.
///
/// Only Active products are priceable. A draft is by definition something
/// nobody has approved for sale yet, and quoting it would let an agent buy
/// through the review queue.
/// </summary>
internal sealed class EfPricedItemReader(TenderoDbContext context, ICategoryReader categories)
    : IPricedItemReader
{
    public async Task<IReadOnlyList<PricedItem>> BySkusAsync(
        IReadOnlyCollection<string> skus, CancellationToken cancellationToken = default)
    {
        if (skus.Count == 0)
            return [];

        // Matching happens in the database, but on the variant rather than the
        // product: SKU is the purchasable unit's name (ADR 0015), and it is also
        // the vocabulary Inventory will use for the same article without sharing
        // an entity with anyone.
        var wanted = skus.Select(sku => sku.Trim()).ToArray();

        var products = await context.Products
            .AsNoTracking()
            .Where(product => product.Status == ProductStatus.Active
                              && product.Variants.Any(variant => wanted.Contains(variant.Sku)))
            .ToListAsync(cancellationToken);

        var tree = await categories.AllAsync(cancellationToken);

        return
        [
            .. from product in products
               from variant in product.Variants
               where wanted.Contains(variant.Sku, StringComparer.OrdinalIgnoreCase)
               select new PricedItem(
                   variant.Id,
                   product.Id,
                   variant.Sku,
                   variant.Price,
                   // The whole branch, HOME/KITCHEN/COOKWARE, so that a
                   // promotion on KITCHEN reaches a pan filed under COOKWARE.
                   // The leaf alone would make every category promotion a
                   // promotion on exactly one shelf.
                   tree.ByCode(product.Category)?.Path,
                   variant.TaxClass)
        ];
    }
}
