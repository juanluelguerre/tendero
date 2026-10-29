using System.Diagnostics.Metrics;
using ElGuerre.Tendero.Catalog.Connectors;
using ElGuerre.Tendero.SharedKernel;

namespace ElGuerre.Tendero.Catalog.Features.ImportProducts;

/// <summary>
/// `tendero.catalog.import.products` and `tendero.catalog.import.notes` (the
/// tracking plan; phase 16, P16-7): is a source used, and how much of it comes
/// across clean. An import is an operator's action, so the proof is a server
/// metric, counted once the batches are in — a product counted as created in a
/// batch that never reached Postgres is a failure, not a creation.
/// </summary>
internal static class ImportMetrics
{
    private static readonly Meter Meter = new(TelemetrySources.Catalog);

    private static readonly Counter<long> Products = Meter.CreateCounter<long>(
        "tendero.catalog.import.products", unit: "{product}", description: "Products an import created, updated or failed, by source.");

    private static readonly Counter<long> Notes = Meter.CreateCounter<long>(
        "tendero.catalog.import.notes", unit: "{note}", description: "What a source did not hand over cleanly, by source and code.");

    public static void Record(string source, int created, int updated, int failed, IEnumerable<SourceNote> notes)
    {
        Add(source, "created", created);
        Add(source, "updated", updated);
        Add(source, "failed", failed);

        foreach (var group in notes.GroupBy(note => note.Code))
        {
            Notes.Add(
                group.Count(),
                new KeyValuePair<string, object?>("catalog.source", source),
                new KeyValuePair<string, object?>("catalog.import.note", group.Key.ToString()));
        }
    }

    private static void Add(string source, string outcome, int count)
    {
        if (count == 0)
            return;

        Products.Add(
            count,
            new KeyValuePair<string, object?>("catalog.source", source),
            new KeyValuePair<string, object?>("catalog.import.outcome", outcome));
    }
}
