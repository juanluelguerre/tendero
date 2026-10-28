using ElGuerre.Tendero.Catalog.Ports;
using ElGuerre.Tendero.Exports.Features.CatalogueForAnotherPlatform;
using ElGuerre.Tendero.Exports.Features.OrdersForTheBooks;
using ElGuerre.Tendero.Exports.Features.ProductFeed;
using ElGuerre.Tendero.Inventory.Ports;
using ElGuerre.Tendero.Ordering.Ports;
using ElGuerre.Tendero.SharedKernel;
using ElGuerre.Tendero.Tests;
using Microsoft.Extensions.Options;
using Xunit;

namespace ElGuerre.Tendero.Exports.Tests;

/// <summary>
/// What proves the exports are used (spec, *Evidence of use*; ADR 0036): each export
/// written counted by its kind and format, with the rows it carried. Found by the
/// exports' Stryker close: no test listened, so the counter could be removed. The
/// instruments are the process's, so each test names a format no other test uses
/// and reads only its own measurements.
/// </summary>
public sealed class ExportMetricsTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Money Eur(decimal amount) => new(amount, "EUR");

    private static readonly ExportedProduct Mug = new(
        "B99MUG0001", true, "Taza", "taza", null, null, [], [], [], [new("MUG-DEFAULT", null, Eur(10m), null, [], [])]);

    [Fact]
    public async Task The_feed_is_counted_by_its_kind_and_format_with_its_rows()
    {
        using var recorder = new InstrumentRecorder("tendero.exports.generated", "tendero.exports.rows");
        var formats = new Named<FeedItem>("metrics-feed");

        await new ProductFeedHandler(new Catalogue([Mug, Mug with { Code = "B99MUG0002" }]), new NoStock(), GoogleCategoryMapping.None,
            Options.Create(new ExportsOptions()), formats).HandleAsync(new ProductFeedQuery("es", "https://api.example"), Ct);

        AssertCounted(recorder, "feed", "metrics-feed", rows: 2);
    }

    [Fact]
    public async Task The_catalogue_is_counted_by_its_kind_and_format_with_its_rows()
    {
        using var recorder = new InstrumentRecorder("tendero.exports.generated", "tendero.exports.rows");
        var formats = new Named<ProductForAnotherPlatform>("metrics-catalogue");

        await new ExportCatalogueHandler(new Catalogue([Mug]), new NoStock(), GoogleCategoryMapping.None, formats)
            .HandleAsync(new ExportCatalogueCommand("metrics-catalogue", "es", "https://api.example"), Ct);

        AssertCounted(recorder, "catalogue", "metrics-catalogue", rows: 1);
    }

    [Fact]
    public async Task Orders_are_counted_by_their_kind_and_format_with_their_rows()
    {
        using var recorder = new InstrumentRecorder("tendero.exports.generated", "tendero.exports.rows");
        var formats = new Named<OrderLineForTheBooks>("metrics-orders");
        var order = new ExportedOrder("o-1", new DateOnly(2026, 10, 2), "Delivered", "Ana", "ES",
            [new("MUG-DEFAULT", "Taza", 1, Eur(10m), Eur(10m))], [], new ExportedOrderShipping("Envío", Eur(4.95m)), [], Eur(14.95m));

        await new ExportOrdersHandler(new Sales([order]), formats)
            .HandleAsync(new ExportOrdersCommand(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31), "metrics-orders", "es"), Ct);

        AssertCounted(recorder, "orders", "metrics-orders", rows: 2);
    }

    private static void AssertCounted(InstrumentRecorder recorder, string kind, string format, int rows)
    {
        bool Ours((long Value, IReadOnlyDictionary<string, object?> Tags) measurement) => Equals(measurement.Tags["export.format"], format);

        var generated = Assert.Single(recorder.Measured("tendero.exports.generated"), Ours);
        Assert.Equal((1L, kind), (generated.Value, generated.Tags["export.kind"]));
        var written = Assert.Single(recorder.Measured("tendero.exports.rows"), Ours);
        Assert.Equal((rows, kind), (written.Value, written.Tags["export.kind"]));
    }

    private sealed class Catalogue(ExportedProduct[] products) : ICatalogueExport
    {
        public Task<IReadOnlyList<ExportedProduct>> ProductsAsync(string culture, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ExportedProduct>>(products);
    }

    private sealed class NoStock : IAvailabilityReader
    {
        public Task<IReadOnlyDictionary<string, int>> AvailableAsync(IReadOnlyCollection<string> skus, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<string, int>>(new Dictionary<string, int>());
    }

    private sealed class Sales(ExportedOrder[] orders) : IOrderExport
    {
        public Task<IReadOnlyList<ExportedOrder>> SalesAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ExportedOrder>>(orders);
    }

    /// <summary>A format that writes nothing, under a name only its test uses, found whatever key is asked.</summary>
    private sealed class Named<TRow>(string name) : IExportFormats, IExportFormat<TRow>
    {
        public string Format => name;

        public string ContentType => "text/plain; charset=utf-8";

        public string Extension => "txt";

        public IExportFormat<T>? Find<T>(string format) => this as IExportFormat<T>;

        public Task WriteAsync(IReadOnlyList<TRow> rows, ExportWriting writing, Stream output, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
