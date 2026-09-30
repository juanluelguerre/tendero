namespace ElGuerre.Tendero.Inventory.Ports;

/// <summary>
/// What sold of a SKU in a window, and since when it has been on sale in the shop
/// — the later of the date its source first listed it and the day the shop had it.
/// </summary>
public sealed record SkuPace(int Units, DateTimeOffset? OnSaleSince);

/// <summary>
/// The pace the shop sells at, which Ordering knows and Inventory needs for stock
/// coverage and sell-through (R-9, D-7). Inventory owns the stock and Ordering the
/// sales, so the port is Inventory's and Persistence answers it from the orders —
/// the way <c>IAvailabilityReader</c> lets Ordering read stock without Inventory's
/// domain. Only paid, live orders count, as in every sales figure.
/// </summary>
public interface ISalesPace
{
    /// <summary>One entry for every SKU asked, a zero when it sold nothing.</summary>
    Task<IReadOnlyDictionary<string, SkuPace>> SoldAsync(
        IReadOnlyCollection<string> skus, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
