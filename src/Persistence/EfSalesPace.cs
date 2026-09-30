using ElGuerre.Tendero.Catalog.Domain;
using ElGuerre.Tendero.Inventory.Ports;
using ElGuerre.Tendero.Ordering;
using ElGuerre.Tendero.Ordering.Domain;
using Microsoft.EntityFrameworkCore;

namespace ElGuerre.Tendero.Persistence;

/// <summary>
/// Inventory's <see cref="ISalesPace"/>, answered from the orders and the catalogue
/// (R-9, D-7). What counts as sold is Ordering's one rule, <see cref="SalesRanking.IsSale"/>
/// — paid and live — read here rather than restated. The lines are jsonb, so the
/// orders of the window are read and their lines summed in memory, as every
/// dashboard read already does at the laboratory's scale.
/// </summary>
internal sealed class EfSalesPace(TenderoDbContext context) : ISalesPace
{
    public async Task<IReadOnlyDictionary<string, SkuPace>> SoldAsync(
        IReadOnlyCollection<string> skus, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        var (since, until) = (from.ToUniversalTime(), to.ToUniversalTime());
        var orders = await context.Orders.AsNoTracking()
            .Where(order => order.CreatedAt >= since && order.CreatedAt < until)
            .ToListAsync(cancellationToken);

        var units = orders
            .Where(SalesRanking.IsSale)
            .SelectMany(order => order.Lines)
            .GroupBy(line => line.Sku, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Sum(line => line.Quantity), StringComparer.OrdinalIgnoreCase);

        var onSale = await OnSaleSinceAsync(skus, cancellationToken);

        return skus.ToDictionary(
            sku => sku,
            sku => new SkuPace(units.GetValueOrDefault(sku), onSale.GetValueOrDefault(sku)),
            StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Since when a SKU has been on sale here: the later of the day its source first
    /// listed it and the day the shop imported it — nothing sells before either.
    /// </summary>
    private async Task<Dictionary<string, DateTimeOffset?>> OnSaleSinceAsync(IReadOnlyCollection<string> skus, CancellationToken cancellationToken)
    {
        var wanted = skus.ToArray();
        var products = await context.Products.AsNoTracking()
            .Where(product => product.Variants.Any(variant => wanted.Contains(variant.Sku)))
            .ToListAsync(cancellationToken);

        return (
            from product in products
            from variant in product.Variants
            where wanted.Contains(variant.Sku, StringComparer.OrdinalIgnoreCase)
            select (variant.Sku, Since: Later(product.CreatedAt, product.AvailableFrom))
        ).ToDictionary(item => item.Sku, item => (DateTimeOffset?)item.Since, StringComparer.OrdinalIgnoreCase);
    }

    private static DateTimeOffset Later(DateTimeOffset imported, DateOnly? listed) =>
        listed is { } day && day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc) is var at && at > imported ? at : imported;
}
