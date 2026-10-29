using System.Diagnostics.Metrics;
using ElGuerre.Tendero.Inventory.Ports;
using ElGuerre.Tendero.SharedKernel;

namespace ElGuerre.Tendero.Inventory.Features.ImportStock;

/// <summary>
/// `tendero.inventory.import.levels` and `tendero.inventory.import.notes` (the
/// tracking plan; phase 16, spec *Evidence of use*): how much of a store's stock
/// comes across, by source, as the catalogue's import counts its products. The
/// source's label is <c>catalog.source</c> in both, so one query reads a store's
/// import across contexts.
/// </summary>
internal static class ImportStockMetrics
{
    private static readonly Meter Meter = new(TelemetrySources.Inventory);

    private static readonly Counter<long> Levels = Meter.CreateCounter<long>(
        "tendero.inventory.import.levels", unit: "{level}", description: "Stock levels an import counted or skipped, by source.");

    private static readonly Counter<long> Notes = Meter.CreateCounter<long>(
        "tendero.inventory.import.notes", unit: "{note}", description: "What a stock source did not hand over cleanly, by source and code.");

    public static void Record(string source, int counted, int skipped, IEnumerable<StockNote> notes)
    {
        Add(source, "counted", counted);
        Add(source, "skipped", skipped);

        foreach (var group in notes.GroupBy(note => note.Code))
        {
            Notes.Add(
                group.Count(),
                new KeyValuePair<string, object?>("catalog.source", source),
                new KeyValuePair<string, object?>("inventory.import.note", group.Key.ToString()));
        }
    }

    private static void Add(string source, string outcome, int count)
    {
        if (count == 0)
            return;

        Levels.Add(
            count,
            new KeyValuePair<string, object?>("catalog.source", source),
            new KeyValuePair<string, object?>("inventory.import.outcome", outcome));
    }
}
