using ElGuerre.Tendero.Inventory.Ports;
using Microsoft.Extensions.DependencyInjection;

// The strategies live beside the ledger and not in a folder of their own: a
// namespace called `Allocation` shadows the `Allocation` record it exists to
// return, and the ledger is the only thing that ever asks for one.
namespace ElGuerre.Tendero.Inventory.Ledger;

/// <summary>
/// Walk the warehouses in preference order and take what each can give, until
/// the line is filled. It will split one line across two warehouses.
///
/// This is the default because it sells the most: with 2 in Madrid and 3 in
/// Barcelona, a request for 4 succeeds. The cost is real and is the reason the
/// other strategy exists — the customer gets two parcels, two carriers and two
/// tracking numbers for one order line, which is more expensive to ship and
/// worse to receive.
/// </summary>
internal sealed class PriorityFirstAllocation : IAllocationStrategy
{
    public const string Key = "priority-first";

    string IAllocationStrategy.Key => Key;

    public IReadOnlyList<Allocation> Allocate(StockRequest request, IReadOnlyList<WarehouseStock> warehouses)
    {
        var remaining = request.Quantity;
        var allocations = new List<Allocation>();

        foreach (var warehouse in Ordered(warehouses))
        {
            if (remaining == 0) break;

            var take = Math.Min(remaining, warehouse.Available);
            if (take <= 0) continue;

            allocations.Add(new Allocation(request.Sku, warehouse.WarehouseCode, take));
            remaining -= take;
        }

        // All or nothing: a half-filled line is stock held for an order that
        // cannot ship.
        return remaining == 0 ? allocations : [];
    }

    /// <summary>
    /// Preference, then code. The tiebreak is not cosmetic: two warehouses of
    /// equal priority would otherwise be picked in whatever order the database
    /// returned them, and the same order would allocate differently on two runs
    /// — which is the same determinism argument the promotion engine makes about
    /// <c>(Priority, Code)</c>.
    /// </summary>
    internal static IEnumerable<WarehouseStock> Ordered(IReadOnlyList<WarehouseStock> warehouses) =>
        warehouses
            .OrderBy(warehouse => warehouse.Priority)
            .ThenBy(warehouse => warehouse.WarehouseCode, StringComparer.Ordinal);
}

/// <summary>
/// One warehouse or nothing: the first, in preference order, that can fill the
/// whole line on its own.
///
/// It sells less than <see cref="PriorityFirstAllocation"/> on purpose. Some
/// goods must not be split — a set of three pans is one parcel, and shipping it
/// as two is a customer complaint waiting to happen — and some shops simply do
/// not want to pay for two consignments.
/// </summary>
internal sealed class SingleWarehouseAllocation : IAllocationStrategy
{
    public const string Key = "single-warehouse";

    string IAllocationStrategy.Key => Key;

    public IReadOnlyList<Allocation> Allocate(StockRequest request, IReadOnlyList<WarehouseStock> warehouses)
    {
        var chosen = PriorityFirstAllocation
            .Ordered(warehouses)
            .FirstOrDefault(warehouse => warehouse.Available >= request.Quantity);

        return chosen is null ? [] : [new Allocation(request.Sku, chosen.WarehouseCode, request.Quantity)];
    }
}

/// <summary>
/// The only place in Inventory that speaks to the container. Same pattern as
/// <c>KeyedCatalogSourceRegistry</c> and <c>KeyedTaxCalculatorRegistry</c>, and
/// for the same reason: the list of keys comes from the registry rather than
/// from a constant, because a constant and a registry are two truths that
/// diverge.
/// </summary>
internal sealed class KeyedAllocationStrategyRegistry(IServiceProvider services) : IAllocationStrategyRegistry
{
    public IReadOnlyCollection<string> Keys =>
        [.. services.GetKeyedServices<IAllocationStrategy>(KeyedService.AnyKey)
            .Select(strategy => strategy.Key)
            .Order(StringComparer.Ordinal)];

    public IAllocationStrategy Get(string key) =>
        services.GetKeyedService<IAllocationStrategy>(key)
        ?? throw new UnknownAllocationStrategyException(key, Keys);
}
