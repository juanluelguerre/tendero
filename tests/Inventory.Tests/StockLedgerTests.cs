using ElGuerre.Tendero.Inventory.Domain;
using ElGuerre.Tendero.Inventory.Ledger;
using ElGuerre.Tendero.Inventory.Ports;
using ElGuerre.Tendero.SharedKernel;
using ElGuerre.Tendero.Tests;
using Microsoft.Extensions.Options;
using Xunit;

namespace ElGuerre.Tendero.Inventory.Tests;

/// <summary>
/// The ledger over deterministic in-memory doubles: what matters about a port is
/// what reached it, and a list says that more clearly than a call verification
/// (docs/testing.md).
/// </summary>
public sealed class StockLedgerTests
{
    private readonly TestClock clock = new();
    private readonly FakeStock stock = new();
    private readonly FakeWarehouses warehouses = new();
    private int saves;

    private StockLedger Ledger(string strategy = PriorityFirstAllocation.Key) => new(
        this.stock, this.warehouses, new FakeStrategies(),
        new CountingUnitOfWork(() => this.saves++),
        Options.Create(new InventoryOptions { AllocationStrategy = strategy }),
        this.clock);

    private static readonly OrderId Order = OrderId.New();

    /// <summary>xUnit v3 wants every awaited call to carry the test's token, so
    /// a cancelled run stops instead of finishing politely.</summary>
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // ---------- Holding ----------

    [Fact]
    public async Task Stock_is_taken_from_the_preferred_warehouse_first()
    {
        this.stock.Put("SHOES", "MAD", 4);
        this.stock.Put("SHOES", "BCN", 9);

        var outcome = await Ledger().ReserveAsync(Order, [new StockRequest("SHOES", 3)], Ct);

        Assert.True(outcome.Reserved);
        Assert.Equal(3, this.stock.Row("SHOES", "MAD").Reserved);
        Assert.Equal(0, this.stock.Row("SHOES", "BCN").Reserved);
    }

    /// <summary>
    /// The board's demo: MAD at zero, BCN with stock, and allocation picks BCN
    /// without anybody configuring anything.
    /// </summary>
    [Fact]
    public async Task An_empty_preferred_warehouse_is_skipped_rather_than_failed()
    {
        this.stock.Put("SHOES", "MAD", 0);
        this.stock.Put("SHOES", "BCN", 3);

        Assert.True((await Ledger().ReserveAsync(Order, [new StockRequest("SHOES", 3)], Ct)).Reserved);
        Assert.Equal(3, this.stock.Row("SHOES", "BCN").Reserved);
    }

    [Fact]
    public async Task Reserving_does_not_move_a_single_box()
    {
        this.stock.Put("SHOES", "MAD", 5);

        await Ledger().ReserveAsync(Order, [new StockRequest("SHOES", 2)], Ct);

        // On hand is what is physically there and a hold does not ship anything.
        // A ledger that decremented here is one that cannot tell a reservation
        // from a shipment, which is how a cancelled order loses its stock.
        Assert.Equal(5, this.stock.Row("SHOES", "MAD").OnHand);
        Assert.Equal(3, this.stock.Row("SHOES", "MAD").Available);
    }

    // ---------- Refusing ----------

    /// <summary>
    /// The most interesting row in the system. A refusal is SAVED, with words,
    /// because the alternative — returning false and writing nothing — leaves
    /// the backoffice unable to say why an order was cancelled.
    /// </summary>
    [Fact]
    public async Task A_refusal_is_written_down_with_the_numbers_in_it()
    {
        this.stock.Put("PANS", "MAD", 0);
        this.stock.Put("PANS", "BCN", 0);

        var outcome = await Ledger().ReserveAsync(Order, [new StockRequest("PANS", 2)], Ct);

        Assert.False(outcome.Reserved);
        Assert.Equal("PANS: 2 asked for, 0 available.", outcome.Reason);

        var reservation = Assert.Single(this.stock.Reservations);
        Assert.Equal(ReservationStatus.Released, reservation.Status);
        Assert.Equal(outcome.Reason, reservation.Reason);
    }

    [Fact]
    public async Task A_sku_nobody_stocks_is_a_different_refusal_from_one_that_ran_out()
    {
        var outcome = await Ledger().ReserveAsync(Order, [new StockRequest("GHOST", 1)], Ct);

        Assert.False(outcome.Reserved);
        Assert.Equal("GHOST is not stocked in any open warehouse.", outcome.Reason);
    }

    /// <summary>
    /// All or nothing across LINES, not just within one. Four lines that can be
    /// filled and a fifth that cannot is an order that cannot ship, and holding
    /// the four would take them off sale for nothing.
    /// </summary>
    [Fact]
    public async Task One_line_that_cannot_be_filled_refuses_the_whole_order()
    {
        this.stock.Put("SHOES", "MAD", 10);
        this.stock.Put("PANS", "MAD", 0);

        var outcome = await Ledger().ReserveAsync(
            Order, [new StockRequest("SHOES", 1), new StockRequest("PANS", 1)], Ct);

        Assert.False(outcome.Reserved);
        Assert.Equal(0, this.stock.Row("SHOES", "MAD").Reserved);
    }

    [Fact]
    public async Task A_closed_warehouse_keeps_its_stock_and_sells_none_of_it()
    {
        this.warehouses.Close("BCN");
        this.stock.Put("SHOES", "MAD", 0);
        this.stock.Put("SHOES", "BCN", 9);

        var outcome = await Ledger().ReserveAsync(Order, [new StockRequest("SHOES", 1)], Ct);

        Assert.False(outcome.Reserved);
        Assert.Equal(9, this.stock.Row("SHOES", "BCN").OnHand);
    }

    /// <summary>
    /// The same SKU on two lines has to see what the first one took. Without
    /// this, an order for 2 + 2 of something there are 3 of would be allocated
    /// twice out of the same stock and hold four.
    /// </summary>
    [Fact]
    public async Task Two_lines_of_the_same_sku_do_not_get_allocated_the_same_stock_twice()
    {
        this.stock.Put("SHOES", "MAD", 3);

        var outcome = await Ledger().ReserveAsync(
            Order, [new StockRequest("SHOES", 2), new StockRequest("SHOES", 2)], Ct);

        Assert.False(outcome.Reserved);
        Assert.Equal(0, this.stock.Row("SHOES", "MAD").Reserved);
    }

    // ---------- Idempotence, because the outbox delivers at least once ----------

    [Fact]
    public async Task Reserving_twice_for_one_order_holds_the_stock_once()
    {
        this.stock.Put("SHOES", "MAD", 5);

        await Ledger().ReserveAsync(Order, [new StockRequest("SHOES", 2)], Ct);
        var second = await Ledger().ReserveAsync(Order, [new StockRequest("SHOES", 2)], Ct);

        Assert.True(second.Reserved);
        Assert.Equal(2, this.stock.Row("SHOES", "MAD").Reserved);
        Assert.Single(this.stock.Reservations);
    }

    // ---------- Compensation: the arm that never runs on the happy path ----------

    [Fact]
    public async Task Releasing_gives_the_hold_back_and_says_why()
    {
        this.stock.Put("SHOES", "MAD", 5);
        await Ledger().ReserveAsync(Order, [new StockRequest("SHOES", 2)], Ct);

        await Ledger().ReleaseAsync(Order, "The customer changed their mind.", Ct);

        Assert.Equal(0, this.stock.Row("SHOES", "MAD").Reserved);
        Assert.Equal(5, this.stock.Row("SHOES", "MAD").Available);

        var reservation = Assert.Single(this.stock.Reservations);
        Assert.Equal(ReservationStatus.Released, reservation.Status);
        Assert.Equal("The customer changed their mind.", reservation.Reason);
    }

    [Fact]
    public async Task Releasing_twice_does_not_manufacture_availability()
    {
        this.stock.Put("SHOES", "MAD", 5);
        await Ledger().ReserveAsync(Order, [new StockRequest("SHOES", 2)], Ct);

        await Ledger().ReleaseAsync(Order, "once", Ct);
        await Ledger().ReleaseAsync(Order, "twice", Ct);

        Assert.Equal(5, this.stock.Row("SHOES", "MAD").Available);
    }

    [Fact]
    public async Task Releasing_an_order_that_never_held_anything_does_nothing()
    {
        await Ledger().ReleaseAsync(OrderId.New(), "nothing to give back", Ct);

        Assert.Empty(this.stock.Reservations);
    }

    // ---------- Committing ----------

    [Fact]
    public async Task Shipping_turns_the_hold_into_a_decrement_without_freeing_anything()
    {
        this.stock.Put("SHOES", "MAD", 5);
        await Ledger().ReserveAsync(Order, [new StockRequest("SHOES", 2)], Ct);

        await Ledger().CommitAsync(Order, Ct);

        var row = this.stock.Row("SHOES", "MAD");
        Assert.Equal(3, row.OnHand);
        Assert.Equal(0, row.Reserved);

        // Available was 3 before shipping and is 3 after: goods that were
        // already spoken for leaving the building make nothing newly available.
        // A commit that only moved OnHand would double-count.
        Assert.Equal(3, row.Available);
    }

    [Fact]
    public async Task Committing_twice_ships_the_goods_once()
    {
        this.stock.Put("SHOES", "MAD", 5);
        await Ledger().ReserveAsync(Order, [new StockRequest("SHOES", 2)], Ct);

        await Ledger().CommitAsync(Order, Ct);
        await Ledger().CommitAsync(Order, Ct);

        Assert.Equal(3, this.stock.Row("SHOES", "MAD").OnHand);
    }

    // ---------- Receiving ----------

    [Fact]
    public async Task Receiving_into_a_warehouse_that_never_held_the_sku_starts_the_row()
    {
        await Ledger().ReceiveAsync("NEW", "MAD", 6, Ct);

        Assert.Equal(6, this.stock.Row("NEW", "MAD").OnHand);
    }

    // ---------- Counting ----------

    /// <summary>
    /// A count replaces; it does not add. That is the difference between a
    /// stocktake and a delivery, and it is what makes pressing save twice
    /// harmless.
    /// </summary>
    [Fact]
    public async Task A_count_replaces_what_the_shelf_held()
    {
        this.stock.Put("SHOES", "MAD", 9);

        var shelf = await Ledger().CountAsync("SHOES", "MAD", 4, Ct);

        Assert.Equal(4, this.stock.Row("SHOES", "MAD").OnHand);
        Assert.Equal((4, 4), (shelf.OnHand, shelf.Available));
    }

    /// <summary>
    /// The one thing receiving cannot express, and the reason counting is a
    /// separate operation rather than <c>Receive</c> with a signed quantity: a
    /// shelf somebody looked at and found empty is a fact, and it is a different
    /// fact from a SKU this warehouse does not stock.
    ///
    /// It is not academic. The seeder filtered its zero rows out because
    /// receiving nothing does nothing, and the out-of-stock product the demo
    /// exists to show simply had no row on the grid.
    /// </summary>
    [Fact]
    public async Task Counting_zero_puts_an_empty_shelf_on_the_record()
    {
        var shelf = await Ledger().CountAsync("PANS", "MAD", 0, Ct);

        Assert.Equal(0, this.stock.Row("PANS", "MAD").OnHand);
        Assert.Equal(0, shelf.Available);
    }

    /// <summary>
    /// Availability is what is left to SELL, so a count that lands under what is
    /// already held answers with the held number subtracted — which is why the
    /// screen redraws from this answer instead of from what was typed.
    /// </summary>
    [Fact]
    public async Task A_count_answers_with_what_is_left_after_the_holds()
    {
        this.stock.Put("SHOES", "MAD", 9);
        await Ledger().ReserveAsync(Order, [new StockRequest("SHOES", 3)], Ct);

        var shelf = await Ledger().CountAsync("SHOES", "MAD", 5, Ct);

        Assert.Equal((5, 2), (shelf.OnHand, shelf.Available));
    }

    // ---------- Strategy is configuration ----------

    [Fact]
    public async Task Refusing_to_split_a_parcel_is_a_line_of_configuration()
    {
        this.stock.Put("SHOES", "MAD", 3);
        this.stock.Put("SHOES", "BCN", 2);

        Assert.True((await Ledger().ReserveAsync(Order, [new StockRequest("SHOES", 5)], Ct)).Reserved);

        this.stock.Reset();
        this.stock.Put("SHOES", "MAD", 3);
        this.stock.Put("SHOES", "BCN", 2);

        var single = await Ledger(SingleWarehouseAllocation.Key)
            .ReserveAsync(OrderId.New(), [new StockRequest("SHOES", 5)], Ct);

        Assert.False(single.Reserved);
    }

    // ---------- Doubles ----------

    private sealed class FakeStock : IStockRepository
    {
        private readonly List<StockItem> items = [];
        private readonly List<Reservation> reservations = [];

        public IReadOnlyList<Reservation> Reservations => this.reservations;

        public void Put(string sku, string warehouse, int onHand)
        {
            var item = StockItem.For(sku, warehouse);
            if (onHand > 0) item.Receive(TimeProvider.System, onHand);
            this.items.Add(item);
        }

        public void Reset()
        {
            this.items.Clear();
            this.reservations.Clear();
        }

        public StockItem Row(string sku, string warehouse) =>
            this.items.Single(item => item.Sku == sku && item.WarehouseCode == warehouse);

        public Task<IReadOnlyList<StockItem>> ForSkusAsync(
            IReadOnlyCollection<string> skus, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<StockItem>>(
                [.. this.items.Where(item => skus.Contains(item.Sku, StringComparer.OrdinalIgnoreCase))]);

        public Task<IReadOnlyList<StockItem>> AllAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<StockItem>>([.. this.items]);

        public Task<StockItem?> FindAsync(
            string sku, string warehouseCode, CancellationToken cancellationToken = default) =>
            Task.FromResult(this.items.FirstOrDefault(item => item.Sku == sku && item.WarehouseCode == warehouseCode));

        public void Add(StockItem item) => this.items.Add(item);

        public Task<Reservation?> FindReservationAsync(
            OrderId orderId, CancellationToken cancellationToken = default) =>
            Task.FromResult(this.reservations.FirstOrDefault(reservation => reservation.OrderId == orderId));

        public Task<IReadOnlyList<Reservation>> RecentReservationsAsync(
            int take, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Reservation>>([.. this.reservations.Take(take)]);

        public void Add(Reservation reservation) => this.reservations.Add(reservation);
    }

    private sealed class FakeWarehouses : IWarehouseReader
    {
        private readonly List<Warehouse> warehouses =
        [
            new("MAD", LocalizedText.From("en", "Madrid"), 10),
            new("BCN", LocalizedText.From("en", "Barcelona"), 20)
        ];

        public void Close(string code) =>
            this.warehouses[this.warehouses.FindIndex(w => w.Code == code)] =
                new Warehouse(code, LocalizedText.From("en", code), 99, isActive: false);

        public Task<IReadOnlyList<Warehouse>> AllAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Warehouse>>([.. this.warehouses]);
    }

    private sealed class FakeStrategies : IAllocationStrategyRegistry
    {
        private readonly Dictionary<string, IAllocationStrategy> strategies = new()
        {
            [PriorityFirstAllocation.Key] = new PriorityFirstAllocation(),
            [SingleWarehouseAllocation.Key] = new SingleWarehouseAllocation()
        };

        public IReadOnlyCollection<string> Keys => this.strategies.Keys;

        public IAllocationStrategy Get(string key) => this.strategies[key];
    }

    private sealed class CountingUnitOfWork(Action onSave) : IUnitOfWork
    {
        public Task SaveChangesAsync(CancellationToken ct)
        {
            onSave();
            return Task.CompletedTask;
        }
    }
}
