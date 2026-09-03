using ElGuerre.Tendero.Inventory.Domain;
using ElGuerre.Tendero.Inventory.Ports;
using ElGuerre.Tendero.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace ElGuerre.Tendero.Persistence;

/// <summary>
/// Stock rows and reservations over EF Core. It implements two ports for the
/// same reason <c>EfProductRepository</c> does: the ledger writes and the
/// backoffice reads, and they are different questions over the same tables.
/// </summary>
internal sealed class EfStockRepository(TenderoDbContext context) : IStockRepository, IAvailabilityReader
{
    // WITH tracking: everything that comes through here is about to be mutated
    // by the ledger and committed through IUnitOfWork. AsNoTracking would lose
    // the change silently in SaveChanges, which is the trap GetByIdAsync and
    // FindByIdAsync exist as two methods to avoid on the catalogue side.
    public async Task<IReadOnlyList<StockItem>> ForSkusAsync(
        IReadOnlyCollection<string> skus, CancellationToken cancellationToken = default) =>
        await context.StockItems
            .Where(item => skus.Contains(item.Sku))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<StockItem>> AllAsync(CancellationToken cancellationToken = default) =>
        await context.StockItems
            .AsNoTracking()
            .OrderBy(item => item.Sku)
            .ThenBy(item => item.WarehouseCode)
            .ToListAsync(cancellationToken);

    public Task<StockItem?> FindAsync(
        string sku, string warehouseCode, CancellationToken cancellationToken = default) =>
        context.StockItems.FirstOrDefaultAsync(
            item => item.Sku == sku && item.WarehouseCode == warehouseCode, cancellationToken);

    public void Add(StockItem item) => context.StockItems.Add(item);

    public Task<Reservation?> FindReservationAsync(
        OrderId orderId, CancellationToken cancellationToken = default) =>
        context.Reservations.FirstOrDefaultAsync(
            reservation => reservation.OrderId == orderId, cancellationToken);

    public async Task<IReadOnlyList<Reservation>> RecentReservationsAsync(
        int take, CancellationToken cancellationToken = default) =>
        await context.Reservations
            .AsNoTracking()
            .OrderByDescending(reservation => reservation.CreatedAt)
            .Take(take)
            .ToListAsync(cancellationToken);

    public void Add(Reservation reservation) => context.Reservations.Add(reservation);

    /// <summary>
    /// Availability summed across warehouses, in SQL.
    ///
    /// The sum happens in the database and not in memory because the caller is
    /// the search projection: it asks about whatever SKUs a product has, on
    /// every reindex, and pulling every row of every warehouse to add two
    /// numbers is the shape that stops working at the point it matters.
    /// </summary>
    public async Task<IReadOnlyDictionary<string, int>> AvailableAsync(
        IReadOnlyCollection<string> skus, CancellationToken cancellationToken = default)
    {
        if (skus.Count == 0)
            return new Dictionary<string, int>();

        var totals = await context.StockItems
            .AsNoTracking()
            .Where(item => skus.Contains(item.Sku))
            .GroupBy(item => item.Sku)
            .Select(group => new { Sku = group.Key, Available = group.Sum(item => item.OnHand - item.Reserved) })
            .ToListAsync(cancellationToken);

        return totals.ToDictionary(total => total.Sku, total => total.Available, StringComparer.OrdinalIgnoreCase);
    }
}
