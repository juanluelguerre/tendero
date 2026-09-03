using ElGuerre.Tendero.SharedKernel;

namespace ElGuerre.Tendero.Inventory.Domain;

/// <summary>
/// The available quantity of one SKU changed. Search projects it into
/// <c>inStock</c>, which is why it carries the total across warehouses rather
/// than the row that moved: a shopper does not care which warehouse it is in.
/// </summary>
public sealed record StockLevelChanged(string Sku, int Available, DateTimeOffset OccurredAt) : IDomainEvent;

/// <summary>
/// How much of one SKU is in one warehouse.
///
/// **Keyed on the SKU, a string, and not on a <c>VariantId</c>.** That single
/// decision is what lets Inventory reference nothing but the SharedKernel: the
/// SKU is the vocabulary the two contexts share, exactly as <c>ProductName</c>
/// is the vocabulary Catalog and Ordering share. A stock row that held a
/// VariantId would be a foreign key to another context's entity, and the
/// architecture rule would be a comment rather than a test.
///
/// It also survives a reimport. The internal id is a GUID v7 minted per import;
/// the SKU is not. A warehouse count keyed on the id would have to be redone
/// every time the catalogue was reimported — the same call the golden set and
/// the price lists already made.
///
/// The consistency boundary is one row: reserving SKU A in MAD contends with
/// nothing that touches SKU B, and that is what makes this an aggregate rather
/// than a table hanging off a warehouse.
/// </summary>
public sealed class StockItem : AggregateRoot
{
    private StockItem() { } // EF Core

    public static StockItem For(string sku, string warehouseCode, int onHand = 0) =>
        new()
        {
            Sku = sku.Trim(),
            WarehouseCode = Warehouse.Normalise(warehouseCode),
            OnHand = onHand >= 0
                ? onHand
                : throw new ArgumentOutOfRangeException(nameof(onHand), "Stock on hand cannot start negative."),
        };

    public string Sku { get; private set; } = default!;
    public string WarehouseCode { get; private set; } = default!;

    /// <summary>What is physically there, reserved or not.</summary>
    public int OnHand { get; private set; }

    /// <summary>
    /// What is spoken for and not yet shipped. It is a separate number from
    /// <see cref="OnHand"/> because a reservation does not move a box: the goods
    /// are still on the shelf, and the difference between the two is the reason
    /// overselling is possible in shops that only count one of them.
    /// </summary>
    public int Reserved { get; private set; }

    /// <summary>What a new order may still take.</summary>
    public int Available => OnHand - Reserved;

    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Goods arrived. The only operation that raises on-hand.</summary>
    public void Receive(TimeProvider clock, int quantity)
    {
        EnsurePositive(quantity);

        OnHand += quantity;
        Touch(clock);
    }

    /// <summary>
    /// A stocktake said the shelf holds a different number than the system did.
    ///
    /// It is a separate operation from <see cref="Receive"/> on purpose, even
    /// though both move <see cref="OnHand"/>: one is goods arriving and the
    /// other is the system being wrong, and a warehouse that cannot tell them
    /// apart cannot explain its own numbers. The count may go below what is
    /// already reserved — that is breakage, and it is a real thing that happens.
    /// </summary>
    public void Adjust(TimeProvider clock, int countedOnHand)
    {
        if (countedOnHand < 0)
            throw new ArgumentOutOfRangeException(nameof(countedOnHand), "A count cannot be negative.");

        OnHand = countedOnHand;
        Touch(clock);
    }

    /// <summary>
    /// Holds quantity for an order. Refuses rather than going negative: the
    /// whole purpose of counting reserved separately is that this can say no.
    /// </summary>
    public void Reserve(TimeProvider clock, int quantity)
    {
        EnsurePositive(quantity);

        if (quantity > Available)
            throw new InsufficientStockException(Sku, WarehouseCode, quantity, Available);

        Reserved += quantity;
        Touch(clock);
    }

    /// <summary>
    /// Gives the hold back. The compensation half of a reservation, and the one
    /// that is easy to leave untested because the happy path never runs it.
    /// </summary>
    public void Release(TimeProvider clock, int quantity)
    {
        EnsurePositive(quantity);

        // Releasing more than is held would manufacture availability out of a
        // bookkeeping error, which is how a shop oversells while its numbers
        // look fine.
        Reserved -= Math.Min(quantity, Reserved);
        Touch(clock);
    }

    /// <summary>
    /// The goods left the building: the hold becomes a real decrement. Both
    /// numbers move at once, which is what keeps <see cref="Available"/>
    /// unchanged — shipping what was already reserved makes nothing newly
    /// available, and a version of this that only touched OnHand would silently
    /// double-count.
    /// </summary>
    public void Commit(TimeProvider clock, int quantity)
    {
        EnsurePositive(quantity);

        var committed = Math.Min(quantity, Reserved);

        Reserved -= committed;
        OnHand -= committed;
        Touch(clock);
    }

    private void Touch(TimeProvider clock)
    {
        UpdatedAt = clock.GetUtcNow();

        // The event carries THIS row's availability; the projection sums the
        // rows for the SKU before writing the search document. Raising it here
        // rather than in the ledger is what makes an adjustment typed into the
        // backoffice reach the index by the same path an order does.
        Raise(new StockLevelChanged(Sku, Available, UpdatedAt));
    }

    private static void EnsurePositive(int quantity)
    {
        if (quantity <= 0)
            throw new ArgumentOutOfRangeException(nameof(quantity), "Quantity must be positive.");
    }
}

/// <summary>
/// There was not enough. Its own type because the saga has to tell this apart
/// from a genuine fault: not enough stock cancels an order with a reason a
/// customer can read, while a broken database retries.
/// </summary>
public sealed class InsufficientStockException(string sku, string warehouseCode, int wanted, int available)
    : InvalidOperationException($"{warehouseCode} has {available} of {sku}, and {wanted} were asked for.")
{
    public string Sku { get; } = sku;
    public string WarehouseCode { get; } = warehouseCode;
    public int Wanted { get; } = wanted;
    public int Available { get; } = available;
}
