using System.Diagnostics;
using ElGuerre.Tendero.Inventory.Domain;
using ElGuerre.Tendero.Inventory.Ports;
using ElGuerre.Tendero.SharedKernel;
using Microsoft.Extensions.Options;

namespace ElGuerre.Tendero.Inventory.Ledger;

public sealed class InventoryOptions
{
    public const string SectionName = "Inventory";

    /// <summary>
    /// Which allocation strategy the shop uses. Changing from splitting a line
    /// across warehouses to refusing to split it is this line of configuration
    /// and not one <c>if</c> anywhere.
    /// </summary>
    public string AllocationStrategy { get; set; } = "priority-first";
}

/// <summary>
/// The four things that ever happen to stock, in one place.
///
/// It is not an adapter to anything external — there is no warehouse management
/// system behind it — so it lives beside the domain rather than in
/// <c>Adapters</c>, the same way Pricing's engine does. What it orchestrates is
/// the repository and the allocation strategy; the arithmetic and the invariants
/// belong to <see cref="StockItem"/>.
///
/// **Reserving is all or nothing, and a refusal is written down.** The obvious
/// implementation refuses by returning false and saving nothing, which is
/// cheaper and loses the only interesting row in the system: a reservation in
/// <c>Released</c> saying "MAD has 0 of B073WXYZ01-DEFAULT, and 2 were asked
/// for". That row is what the backoffice shows, what the order's cancellation
/// reason is copied from, and what makes the phase's demo a thing you can look
/// at rather than a log line.
/// </summary>
public sealed class StockLedger(
    IStockRepository stock,
    IWarehouseReader warehouses,
    IAllocationStrategyRegistry strategies,
    IUnitOfWork unitOfWork,
    IOptions<InventoryOptions> options,
    TimeProvider clock) : IStockLedger
{
    private static readonly ActivitySource Telemetry = new(TelemetrySources.Inventory);

    public async Task<ReservationOutcome> ReserveAsync(
        OrderId orderId, IReadOnlyList<StockRequest> requests, CancellationToken cancellationToken = default)
    {
        using var activity = Telemetry.StartActivity("inventory.reserve");
        activity?.SetTag("inventory.order_id", orderId.Value);
        activity?.SetTag("inventory.line_count", requests.Count);

        // Reserving twice for the same order is what a retried checkout looks
        // like, and with agents a retry is the normal case rather than the rare
        // one. The existing hold is the answer.
        if (await stock.FindReservationAsync(orderId, cancellationToken) is { } existing)
        {
            activity?.SetTag("inventory.reservation", "already-" + existing.Status);

            return existing.Status == ReservationStatus.Held
                ? ReservationOutcome.Held
                : ReservationOutcome.Refused(existing.Reason ?? "The stock for this order is no longer held.");
        }

        var available = await AvailabilityAsync(requests, cancellationToken);
        var strategy = strategies.Get(options.Value.AllocationStrategy);

        var lines = new List<ReservationLine>();

        foreach (var request in requests)
        {
            var allocations = strategy.Allocate(request, available.GetValueOrDefault(request.Sku, []));

            if (allocations.Count == 0)
                return await RefuseAsync(orderId, Explain(request, available), activity, cancellationToken);

            lines.AddRange(allocations.Select(
                allocation => new ReservationLine(allocation.Sku, allocation.WarehouseCode, allocation.Quantity)));

            // The next line has to see what this one took, or an order for two
            // of the same SKU on two lines would be allocated twice out of the
            // same stock.
            available[request.Sku] = Deduct(available[request.Sku], allocations);
        }

        var rows = await stock.ForSkusAsync([.. requests.Select(request => request.Sku)], cancellationToken);

        foreach (var line in lines)
            Row(rows, line.Sku, line.WarehouseCode).Reserve(clock, line.Quantity);

        stock.Add(Reservation.Hold(clock, orderId, lines));
        await unitOfWork.SaveChangesAsync(cancellationToken);

        activity?.SetTag("inventory.reservation", "held");
        return ReservationOutcome.Held;
    }

    public async Task CommitAsync(OrderId orderId, CancellationToken cancellationToken = default)
    {
        using var activity = Telemetry.StartActivity("inventory.commit");
        activity?.SetTag("inventory.order_id", orderId.Value);

        // Not held any more means somebody already committed or released it.
        // Doing nothing is the right answer: the outbox delivers at least once,
        // so every handler in this system has to survive being run twice.
        if (await Held(orderId, cancellationToken) is not { } reservation)
            return;

        var rows = await stock.ForSkusAsync([.. reservation.Lines.Select(line => line.Sku)], cancellationToken);

        foreach (var line in reservation.Lines)
            Row(rows, line.Sku, line.WarehouseCode).Commit(clock, line.Quantity);

        reservation.Commit(clock);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task ReleaseAsync(
        OrderId orderId, string reason, CancellationToken cancellationToken = default)
    {
        using var activity = Telemetry.StartActivity("inventory.release");
        activity?.SetTag("inventory.order_id", orderId.Value);

        if (await Held(orderId, cancellationToken) is not { } reservation)
            return;

        var rows = await stock.ForSkusAsync([.. reservation.Lines.Select(line => line.Sku)], cancellationToken);

        foreach (var line in reservation.Lines)
            Row(rows, line.Sku, line.WarehouseCode).Release(clock, line.Quantity);

        reservation.Release(clock, reason);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task ReceiveAsync(
        string sku, string warehouseCode, int quantity, CancellationToken cancellationToken = default)
    {
        var row = await Shelf(sku, warehouseCode, cancellationToken);

        row.Receive(clock, quantity);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task<CountedShelf> CountAsync(
        string sku, string warehouseCode, int onHand, CancellationToken cancellationToken = default)
    {
        var row = await Shelf(sku, warehouseCode, cancellationToken);

        row.Adjust(clock, onHand);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new CountedShelf(row.OnHand, row.Available);
    }

    /// <summary>
    /// The shelf, created if this SKU has never been in this warehouse.
    ///
    /// A first delivery and a first count are both normal, and neither is an
    /// error: this is how a SKU first appears in a warehouse. Refusing would
    /// mean the only way to stock something new is a delivery note.
    /// </summary>
    private async Task<StockItem> Shelf(
        string sku, string warehouseCode, CancellationToken cancellationToken)
    {
        var row = await stock.FindAsync(sku, warehouseCode, cancellationToken);

        if (row is not null)
            return row;

        row = StockItem.For(sku, warehouseCode);
        stock.Add(row);

        return row;
    }

    private async Task<Reservation?> Held(OrderId orderId, CancellationToken cancellationToken)
    {
        var reservation = await stock.FindReservationAsync(orderId, cancellationToken);
        return reservation?.Status == ReservationStatus.Held ? reservation : null;
    }

    private async Task<ReservationOutcome> RefuseAsync(
        OrderId orderId, string reason, Activity? activity, CancellationToken cancellationToken)
    {
        activity?.SetTag("inventory.reservation", "refused");
        activity?.SetTag("inventory.refusal_reason", reason);

        stock.Add(Reservation.Refused(clock, orderId, reason));
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return ReservationOutcome.Refused(reason);
    }

    private async Task<Dictionary<string, List<WarehouseStock>>> AvailabilityAsync(
        IReadOnlyList<StockRequest> requests, CancellationToken cancellationToken)
    {
        var skus = requests.Select(request => request.Sku).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

        var rows = await stock.ForSkusAsync(skus, cancellationToken);
        var open = (await warehouses.AllAsync(cancellationToken))
            .Where(warehouse => warehouse.IsActive)
            .ToDictionary(warehouse => warehouse.Code, StringComparer.OrdinalIgnoreCase);

        return skus.ToDictionary(
            sku => sku,
            sku => rows
                .Where(row => string.Equals(row.Sku, sku, StringComparison.OrdinalIgnoreCase))
                // A row in a closed warehouse is not availability. Its stock is
                // still counted and still shown; it is just not for sale today.
                .Where(row => open.ContainsKey(row.WarehouseCode))
                .Select(row => new WarehouseStock(
                    row.WarehouseCode, open[row.WarehouseCode].Priority, row.Available))
                .ToList(),
            StringComparer.OrdinalIgnoreCase);
    }

    private static List<WarehouseStock> Deduct(
        List<WarehouseStock> warehouses, IReadOnlyList<Allocation> allocations) =>
        [.. warehouses.Select(warehouse => warehouse with
        {
            Available = warehouse.Available - allocations
                .Where(allocation => allocation.WarehouseCode == warehouse.WarehouseCode)
                .Sum(allocation => allocation.Quantity)
        })];

    private static StockItem Row(IReadOnlyList<StockItem> rows, string sku, string warehouseCode) =>
        rows.First(row =>
            string.Equals(row.Sku, sku, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(row.WarehouseCode, warehouseCode, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Why a line could not be filled, in words rather than a code.
    ///
    /// "We have 1 and you asked for 3" and "we do not stock this at all" are
    /// different problems for whoever reads the cancelled order, and a single
    /// "out of stock" hides which one happened.
    /// </summary>
    private static string Explain(
        StockRequest request, IReadOnlyDictionary<string, List<WarehouseStock>> available)
    {
        var rows = available.GetValueOrDefault(request.Sku, []);
        var total = rows.Sum(row => row.Available);

        return rows.Count == 0
            ? $"{request.Sku} is not stocked in any open warehouse."
            : $"{request.Sku}: {request.Quantity} asked for, {total} available.";
    }
}
