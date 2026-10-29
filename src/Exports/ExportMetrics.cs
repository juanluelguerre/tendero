using System.Diagnostics.Metrics;
using ElGuerre.Tendero.SharedKernel;

namespace ElGuerre.Tendero.Exports;

/// <summary>What leaves the shop, by kind: the tracking plan's <c>export.kind</c>.</summary>
public enum ExportKind
{
    Feed,
    Catalogue,
    Orders,
}

/// <summary>
/// `tendero.exports.generated` and `tendero.exports.rows` (the tracking plan, ADR 0036):
/// each export written, by kind and format, and how many rows it carried. Which
/// exports are used, and in which format, decides whether XLSX earns its dependency
/// and whether a Merchant API client is worth building over a fetched file
/// (spec, *Evidence of use*).
/// </summary>
internal static class ExportMetrics
{
    private static readonly Meter Meter = new(TelemetrySources.Exports);

    private static readonly Counter<long> Generated = Meter.CreateCounter<long>(
        "tendero.exports.generated", unit: "{export}", description: "Exports and feeds written, by kind and format.");

    private static readonly Histogram<long> Rows = Meter.CreateHistogram<long>(
        "tendero.exports.rows", unit: "{row}", description: "The rows an export or a feed carried, by kind and format.");

    public static void Record(ExportKind kind, string format, int rows)
    {
        KeyValuePair<string, object?>[] tags =
        [
            new("export.kind", kind switch
            {
                ExportKind.Feed => "feed",
                ExportKind.Catalogue => "catalogue",
                ExportKind.Orders => "orders",
            }),
            new("export.format", format),
        ];
        Generated.Add(1, tags);
        Rows.Record(rows, tags);
    }
}
