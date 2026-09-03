using ElGuerre.Tendero.Inventory.Domain;
using ElGuerre.Tendero.SharedKernel;

namespace ElGuerre.Tendero.Inventory.Ports;

/// <summary>
/// What a reservation attempt came back with. It carries the reason when it
/// failed, because the caller's next move is to cancel an order with words a
/// customer will read.
/// </summary>
public sealed record ReservationOutcome(bool Reserved, string? Reason)
{
    public static readonly ReservationOutcome Held = new(true, null);

    public static ReservationOutcome Refused(string reason) => new(false, reason);
}

/// <summary>
/// The five things that ever happen to stock: it arrives, it is counted, it gets
/// held, the hold is given back, and the hold becomes a shipment.
///
/// It is the port Ordering talks to, and it speaks in **values** — a SKU, a
/// quantity, an <see cref="OrderId"/> from the SharedKernel. Ordering never sees
/// a <c>StockItem</c> or a <c>Reservation</c>, which is what keeps the crossing
/// to what ADR 0014 permits and lets an architecture rule prove it.
///
/// Reserve is **atomic**: it holds everything or it holds nothing and says why.
/// A partially filled order is stock removed from sale for a shipment that
/// cannot go out.
/// </summary>
public interface IStockLedger
{
    Task<ReservationOutcome> ReserveAsync(
        OrderId orderId, IReadOnlyList<StockRequest> requests, CancellationToken cancellationToken = default);

    /// <summary>The goods left the building. Idempotent: a second call on a
    /// reservation that is no longer held does nothing.</summary>
    Task CommitAsync(OrderId orderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Whether this order currently has stock held for it.
    ///
    /// A question and not a state machine: the caller is another context's
    /// handler deciding whether an order can be confirmed, and it must be able
    /// to ask without ever seeing a <c>Reservation</c>. Values in, a boolean out.
    /// </summary>
    Task<bool> IsHeldAsync(OrderId orderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Give the hold back. This is the compensation arm of the saga, and the one
    /// that never runs on the happy path — which is exactly why it gets its own
    /// test rather than being assumed.
    /// </summary>
    Task ReleaseAsync(OrderId orderId, string reason, CancellationToken cancellationToken = default);

    /// <summary>Goods arrived: add them to what is already there.</summary>
    Task ReceiveAsync(
        string sku, string warehouseCode, int quantity, CancellationToken cancellationToken = default);

    /// <summary>
    /// A stocktake: the shelf holds this many, whatever the system thought.
    ///
    /// Separate from <see cref="ReceiveAsync"/> because they are different facts
    /// — goods arriving versus the system having been wrong — and a warehouse
    /// that cannot tell them apart cannot explain its own numbers. It is also
    /// the only one of the two that can express **zero**: receiving nothing is a
    /// delivery that did not happen, while counting zero is a shelf somebody
    /// looked at. That distinction is what puts an out-of-stock row on the grid
    /// instead of no row at all.
    ///
    /// Returns the shelf after the count, because availability depends on holds
    /// the caller does not track.
    /// </summary>
    Task<CountedShelf> CountAsync(
        string sku, string warehouseCode, int onHand, CancellationToken cancellationToken = default);
}

/// <summary>A shelf after a count: what is there, and what is left to sell.</summary>
public sealed record CountedShelf(int OnHand, int Available);

/// <summary>
/// How much of each SKU can still be sold, summed across warehouses.
///
/// Separate from the ledger because it has a different consumer and a different
/// shape: the ledger is written by a saga, this is read by a projection and by a
/// screen. Same split as <c>IProductRepository</c> and <c>IProductCatalogReader</c>.
/// </summary>
public interface IAvailabilityReader
{
    Task<IReadOnlyDictionary<string, int>> AvailableAsync(
        IReadOnlyCollection<string> skus, CancellationToken cancellationToken = default);
}

/// <summary>Stock rows and reservations, for the ledger and for the backoffice.</summary>
public interface IStockRepository
{
    Task<IReadOnlyList<StockItem>> ForSkusAsync(
        IReadOnlyCollection<string> skus, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<StockItem>> AllAsync(CancellationToken cancellationToken = default);

    Task<StockItem?> FindAsync(string sku, string warehouseCode, CancellationToken cancellationToken = default);

    void Add(StockItem item);

    Task<Reservation?> FindReservationAsync(OrderId orderId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Reservation>> RecentReservationsAsync(
        int take, CancellationToken cancellationToken = default);

    void Add(Reservation reservation);
}

/// <summary>The warehouses, in preference order. Two rows that change about as
/// often as the attribute definitions, so the seed file is the right home until
/// somebody can edit one.</summary>
public interface IWarehouseReader
{
    Task<IReadOnlyList<Warehouse>> AllAsync(CancellationToken cancellationToken = default);
}
