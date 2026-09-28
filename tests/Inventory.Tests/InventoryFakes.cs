using ElGuerre.Tendero.Inventory.Domain;
using ElGuerre.Tendero.Inventory.Ports;
using ElGuerre.Tendero.SharedKernel;

namespace ElGuerre.Tendero.Inventory.Tests;

// The ledger's doubles, shared by every slice test that needs a shelf. They were
// nested inside StockLedgerTests while it was their only caller; the stock list
// (R-7.1) made it two, and a fake copied is a fake that drifts.

internal sealed class FakeStock : IStockRepository
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

internal sealed class FakeWarehouses : IWarehouseReader
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
