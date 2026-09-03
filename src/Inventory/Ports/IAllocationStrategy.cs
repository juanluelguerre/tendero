namespace ElGuerre.Tendero.Inventory.Ports;

/// <summary>How much of one SKU an order wants.</summary>
public sealed record StockRequest(string Sku, int Quantity);

/// <summary>What one warehouse currently has of one SKU, in preference order.</summary>
public sealed record WarehouseStock(string WarehouseCode, int Priority, int Available);

/// <summary>Take this much of this SKU from this warehouse.</summary>
public sealed record Allocation(string Sku, string WarehouseCode, int Quantity);

/// <summary>
/// Which warehouse a line is taken from.
///
/// It is a port with keyed adapters (ADR 0003) because this is the decision a
/// real shop changes without changing anything else: ship from the nearest, ship
/// from the fullest, never split a parcel, prefer the warehouse that is open.
/// Two are enough to prove the seam, and they genuinely disagree — a contract
/// suite with one adapter only proves the adapter agrees with itself.
///
/// **Pure.** Values in, values out, no repository and no clock, which is what
/// lets the contract suite generate awkward stock levels instead of arranging
/// them in a database.
/// </summary>
public interface IAllocationStrategy
{
    /// <summary>The key it registers under, exposed by the adapter so the
    /// registry can be listed from the container rather than from a constant.</summary>
    string Key { get; }

    /// <summary>
    /// The allocations that satisfy the request, or an empty list when it cannot
    /// be satisfied in full.
    ///
    /// Empty rather than partial is the contract, and it is the whole safety
    /// property: a half-filled line is stock held for an order that cannot ship.
    /// Deciding that here rather than in the ledger keeps every strategy honest
    /// about the same thing.
    /// </summary>
    IReadOnlyList<Allocation> Allocate(StockRequest request, IReadOnlyList<WarehouseStock> warehouses);
}

public sealed class UnknownAllocationStrategyException(string key, IReadOnlyCollection<string> known)
    : InvalidOperationException($"Unknown allocation strategy '{key}'. Known: {string.Join(", ", known)}.")
{
    public string Key { get; } = key;
    public IReadOnlyCollection<string> Known { get; } = known;
}

/// <summary>The only place in Inventory allowed to talk to the container, the
/// same arrangement as the catalogue connectors and the tax calculators.</summary>
public interface IAllocationStrategyRegistry
{
    IReadOnlyCollection<string> Keys { get; }
    IAllocationStrategy Get(string key);
}
