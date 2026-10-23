using System.Runtime.CompilerServices;
using ElGuerre.Tendero.Inventory.Features.ImportStock;
using ElGuerre.Tendero.Inventory.Ports;
using ElGuerre.Tendero.Inventory.Tests.Sources;
using ElGuerre.Tendero.SharedKernel;
using Xunit;

namespace ElGuerre.Tendero.Inventory.Tests;

/// <summary>A stock source written in memory: the contract suite's control, and the slice's.</summary>
internal sealed class FakeStockSource : IStockSourceConnector
{
    public const string Key = "fake";

    public List<SourceWarehouse> Warehouses { get; } = [new("MAD", "Almacén central (Madrid)", true), new("VLC", "Almacén Valencia", true)];

    public List<SourceStockLevel> Levels { get; } = [new("SKU-1", "MAD", 4), new("SKU-1", "VLC", 2), new("SKU-2", "MAD", 0)];

    public List<StockNote> Notes { get; } = [];

    public string Source => Key;

    public IReadOnlyList<StockNote> SourceNotes => Notes;

    public async IAsyncEnumerable<SourceWarehouse> StreamWarehousesAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;
        foreach (var warehouse in Warehouses)
            yield return warehouse;
    }

    public async IAsyncEnumerable<SourceStockLevel> StreamLevelsAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;
        foreach (var level in Levels)
            yield return level;
    }
}

public sealed class FakeStockSourceContractTests : StockSourceConnectorContractTests
{
    protected override IStockSourceConnector CreateConnector() => new FakeStockSource();
}

/// <summary>
/// Bringing a source's stock (phase 16, S2a, P16-10): each level enters the
/// ledger as a **count** — what the shelf holds, whatever the system thought,
/// zero included — and only in a warehouse the shop operates. The source's
/// places the shop does not have come back as proposed warehouses, for a person
/// to add to the warehouses file: nothing writes warehouses (spec, corrected).
/// </summary>
public sealed class ImportStockTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static (ImportStockHandler Handler, CountingLedger Ledger, FakeStockSource Source) AnImport()
    {
        var source = new FakeStockSource();
        var ledger = new CountingLedger();
        return (new ImportStockHandler(new OneStockSource(source), ledger, new FakeWarehouses()), ledger, source);
    }

    [Fact]
    public async Task Each_level_in_a_warehouse_the_shop_operates_is_counted_zero_included()
    {
        var (handler, ledger, _) = AnImport();

        var result = await handler.HandleAsync(new ImportStockCommand(FakeStockSource.Key), Ct);

        Assert.Equal([("SKU-1", "MAD", 4), ("SKU-2", "MAD", 0)], ledger.Counts);
        Assert.Equal((2, 1), (result.Counted, result.Skipped));
    }

    /// <summary>
    /// A level in a place the shop does not operate is not counted — it would
    /// create a shelf in a building that does not exist — and is said; the place
    /// comes back as a proposed warehouse.
    /// </summary>
    [Fact]
    public async Task A_place_the_shop_does_not_operate_is_proposed_and_its_levels_said_not_counted()
    {
        var (handler, _, _) = AnImport();

        var result = await handler.HandleAsync(new ImportStockCommand(FakeStockSource.Key, "en"), Ct);

        Assert.Equal([new ProposedWarehouse("VLC", "Almacén Valencia", true)], result.ProposedWarehouses);
        var note = Assert.Single(result.Notes);
        Assert.Equal((StockNoteCode.WarehouseUnknown, "SKU-1"), (note.Code, note.About));
        Assert.Contains("VLC", note.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task What_the_source_could_not_hand_over_comes_back_in_the_language_asked()
    {
        var (handler, _, source) = AnImport();
        source.Notes.Add(new StockNote(StockNoteCode.LocationNotMapped, null, "Tienda de Bilbao"));

        var spanish = await handler.HandleAsync(new ImportStockCommand(FakeStockSource.Key, "es"), Ct);

        var note = spanish.Notes.Single(each => each.Code == StockNoteCode.LocationNotMapped);
        Assert.StartsWith("La ubicación «Tienda de Bilbao»", note.Text, StringComparison.Ordinal);
    }

    private sealed class OneStockSource(IStockSourceConnector source) : IStockSourceRegistry
    {
        public IReadOnlyCollection<string> Sources => [source.Source];

        public IStockSourceConnector Get(string name) => source;
    }

    /// <summary>The ledger as the slice uses it: a count, recorded.</summary>
    private sealed class CountingLedger : IStockLedger
    {
        public List<(string Sku, string Warehouse, int OnHand)> Counts { get; } = [];

        public Task<CountedShelf> CountAsync(string sku, string warehouseCode, int onHand, CancellationToken cancellationToken = default)
        {
            Counts.Add((sku, warehouseCode, onHand));
            return Task.FromResult(new CountedShelf(onHand, onHand));
        }

        public Task<ReservationOutcome> ReserveAsync(OrderId orderId, IReadOnlyList<StockRequest> requests, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task CommitAsync(OrderId orderId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> IsHeldAsync(OrderId orderId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task ReleaseAsync(OrderId orderId, string reason, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task ReceiveAsync(string sku, string warehouseCode, int quantity, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
