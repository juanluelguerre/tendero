namespace ElGuerre.Tendero.Inventory.Ports;

/// <summary>
/// The stock's inbound port (phase 16, S2a): a source that keeps stock — a store
/// a merchant is leaving — tells what each of its places holds of each SKU. Each
/// source registers as a keyed service under its name, like the catalogue's
/// sources (ADR 0003), and the ImportStock slice knows only this interface.
///
/// A source speaks the shop's words, not its own: a SKU as this shop sells it and
/// a warehouse's code. Turning its variants into SKUs and its places into codes
/// is the adapter's job, so Inventory still references the SharedKernel alone.
/// </summary>
public interface IStockSourceConnector
{
    /// <summary>A stable lowercase identifier: "shopify"…</summary>
    string Source { get; }

    /// <summary>The source's places, as warehouses the shop could keep.</summary>
    IAsyncEnumerable<SourceWarehouse> StreamWarehousesAsync(CancellationToken cancellationToken = default);

    /// <summary>What each place holds of each SKU: counted, so zero is a shelf someone looked at.</summary>
    IAsyncEnumerable<SourceStockLevel> StreamLevelsAsync(CancellationToken cancellationToken = default);

    /// <summary>What the source could not hand over cleanly, known once the levels have been read.</summary>
    IReadOnlyList<StockNote> SourceNotes => [];
}

/// <summary>A place the source keeps stock in, by the code the shop would know it by.</summary>
public sealed record SourceWarehouse(string Code, string Name, bool IsActive);

/// <summary>How many of a SKU a warehouse holds, counted.</summary>
public sealed record SourceStockLevel(string Sku, string WarehouseCode, int OnHand);

/// <summary>What did not come across cleanly from a stock source.</summary>
public enum StockNoteCode
{
    /// <summary>A place of the source has no warehouse code; its stock was not read.</summary>
    LocationNotMapped,

    /// <summary>A variant the source counts is not in the catalogue; import the catalogue first.</summary>
    VariantNotInCatalogue,

    /// <summary>A level names a warehouse the shop does not operate; it was not counted.</summary>
    WarehouseUnknown,
}

/// <summary>One note: its code, what it is about, and the source's detail.</summary>
public sealed record StockNote(StockNoteCode Code, string? About, string? Detail);

/// <summary>The stock sources registered, by name; the only place that talks to the container.</summary>
public interface IStockSourceRegistry
{
    IReadOnlyCollection<string> Sources { get; }

    IStockSourceConnector Get(string source);
}
