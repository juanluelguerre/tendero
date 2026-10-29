using ElGuerre.Tendero.Exports.Features.OrdersForTheBooks;
using ElGuerre.Tendero.Ordering.Ports;
using ElGuerre.Tendero.SharedKernel;
using Xunit;

namespace ElGuerre.Tendero.Exports.Tests;

/// <summary>
/// Orders for the accountant (E-4; spec, *Orders for the accountant* and *Measures*):
/// the sales of a span of the shop's days, a row per line, discount, shipping and tax
/// rate, and the amounts summed per order equal to what the order took.
/// </summary>
public sealed class ExportOrdersTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Money Eur(decimal amount) => new(amount, "EUR");

    private static readonly ExportedOrder Moka = new(
        "0192f1a2", new DateOnly(2026, 10, 2), "Delivered", "Lucía Martín", "ES",
        [new("B11COF0401-DEFAULT", "Cafetera italiana", 2, Eur(29.90m), Eur(59.80m)), new("B12LAM0608-DEFAULT", "Bombillas LED E27", 1, Eur(19.90m), Eur(19.90m))],
        [new("WELCOME5", "Bienvenida", Eur(5m))],
        new("Envío estándar", Eur(4.95m)),
        [new("standard", 0.21m, Eur(65.08m), Eur(13.67m))],
        Eur(79.65m));

    private static readonly ExportedOrder FreeShipping = new(
        "0192f1b7", new DateOnly(2026, 10, 3), "Confirmed", "Jordi Puig", "ES",
        [new("B13BAG0702-DEFAULT", "Mochila Cumbre 30L", 1, Eur(109m), Eur(109m))],
        [],
        new("Envío gratuito", Eur(0m)),
        [new("standard", 0.21m, Eur(90.08m), Eur(18.92m))],
        Eur(109m));

    private static (ExportOrdersHandler Handler, RecordingSales Sales, CapturingFormat Format) Compose(params ExportedOrder[] orders)
    {
        var sales = new RecordingSales(orders);
        var format = new CapturingFormat();
        return (new ExportOrdersHandler(sales, format), sales, format);
    }

    [Fact]
    public async Task An_order_leaves_as_its_lines_its_discounts_its_shipping_and_its_tax()
    {
        var (handler, _, format) = Compose(Moka);

        var file = await handler.HandleAsync(new ExportOrdersCommand(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31), "csv", "es"), Ct);

        Assert.Equal(
            [BookLineKind.Line, BookLineKind.Line, BookLineKind.Discount, BookLineKind.Shipping, BookLineKind.Tax],
            format.Written.Select(row => row.Kind));
        Assert.All(format.Written, row => Assert.Equal(("0192f1a2", new DateOnly(2026, 10, 2), "Delivered", "Lucía Martín", "ES"), (row.OrderId, row.Day, row.Status, row.Customer, row.Country)));
        Assert.Equal(("WELCOME5 Bienvenida", Eur(-5m)), (format.Written[2].Description, format.Written[2].Amount));
        Assert.Equal(("Envío estándar", Eur(4.95m)), (format.Written[3].Description, format.Written[3].Amount));
        var tax = format.Written[4];
        Assert.Equal(("standard", (Money?)null, 0.21m, Eur(65.08m), Eur(13.67m)), (tax.Description, tax.Amount, tax.TaxRate!.Value, tax.TaxBase!.Value, tax.Tax!.Value));
        Assert.Equal((5, "orders-2026-10-01-2026-10-31.csv"), (file.Rows, file.FileName));
    }

    /// <summary>The spec's measure: each order's amounts sum to its total, and its tax rows to its tax.</summary>
    [Fact]
    public async Task Each_orders_amounts_add_up_to_what_it_took()
    {
        var (handler, _, format) = Compose(Moka, FreeShipping);

        await handler.HandleAsync(new ExportOrdersCommand(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31), "csv", "en"), Ct);

        foreach (var order in new[] { Moka, FreeShipping })
        {
            var rows = format.Written.Where(row => row.OrderId == order.OrderId).ToList();
            Assert.Equal(order.Total.Amount, rows.Sum(row => row.Amount?.Amount ?? 0m));
            Assert.Equal(order.Taxes.Sum(each => each.Amount.Amount), rows.Sum(row => row.Tax?.Amount ?? 0m));
        }
    }

    /// <summary>Shipping that cost nothing is not a row: it would add a line the accountant books nothing on.</summary>
    [Fact]
    public async Task Free_shipping_is_not_a_row()
    {
        var (handler, _, format) = Compose(FreeShipping);

        await handler.HandleAsync(new ExportOrdersCommand(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31), "csv", "es"), Ct);

        Assert.DoesNotContain(format.Written, row => row.Kind == BookLineKind.Shipping);
    }

    [Fact]
    public async Task The_span_asked_is_the_span_read()
    {
        var (handler, sales, _) = Compose();

        await handler.HandleAsync(new ExportOrdersCommand(new DateOnly(2026, 1, 1), new DateOnly(2026, 3, 31), "csv", "es"), Ct);

        Assert.Equal((new DateOnly(2026, 1, 1), new DateOnly(2026, 3, 31)), sales.Asked);
    }

    [Theory]
    [InlineData("2026-10-01", "2026-10-31", "csv", "es", true)]
    [InlineData("2026-10-01", "2026-10-01", "csv", "en", true)]
    [InlineData("2026-10-31", "2026-10-01", "csv", "es", false)]
    [InlineData("2026-10-01", "2026-10-31", "xlsx", "es", false)]
    [InlineData("2026-10-01", "2026-10-31", "csv", "fr", false)]
    public void A_span_that_ends_after_it_starts_in_a_known_format_and_language_is_asked_for(string from, string to, string format, string culture, bool valid) =>
        Assert.Equal(valid, new ExportOrdersValidator(new KnownFormats()).Validate(new ExportOrdersCommand(DateOnly.Parse(from), DateOnly.Parse(to), format, culture)).IsValid);

    private sealed class RecordingSales(ExportedOrder[] orders) : IOrderExport
    {
        public (DateOnly From, DateOnly To)? Asked { get; private set; }

        public Task<IReadOnlyList<ExportedOrder>> SalesAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
        {
            Asked = (from, to);
            return Task.FromResult<IReadOnlyList<ExportedOrder>>(orders);
        }
    }

    private sealed class KnownFormats : IExportFormats
    {
        public IExportFormat<TRow>? Find<TRow>(string format) =>
            format == OrdersForTheBooksCsv.Key ? (IExportFormat<TRow>)(object)new OrdersForTheBooksCsv() : null;
    }

    private sealed class CapturingFormat : IExportFormats, IExportFormat<OrderLineForTheBooks>
    {
        public List<OrderLineForTheBooks> Written { get; } = [];

        public string Format => OrdersForTheBooksCsv.Key;

        public string ContentType => "text/csv; charset=utf-8";

        public string Extension => "csv";

        public IExportFormat<TRow>? Find<TRow>(string format) => format == Format && this is IExportFormat<TRow> found ? found : null;

        public Task WriteAsync(IReadOnlyList<OrderLineForTheBooks> rows, ExportWriting writing, Stream output, CancellationToken cancellationToken = default)
        {
            Written.AddRange(rows);
            return Task.CompletedTask;
        }
    }
}
