using ElGuerre.Tendero.Exports.Features.OrdersForTheBooks;
using ElGuerre.Tendero.SharedKernel;
using Xunit;

namespace ElGuerre.Tendero.Exports.Tests;

/// <summary>The accountant's CSV passes the contract every format does.</summary>
public sealed class OrdersForTheBooksCsvContractTests : ExportFormatContractTests<OrderLineForTheBooks>
{
    protected override IExportFormat<OrderLineForTheBooks> CreateFormat() => new OrdersForTheBooksCsv();

    protected override IReadOnlyList<OrderLineForTheBooks> Rows() => [OrdersForTheBooksCsvTests.Moka, OrdersForTheBooksCsvTests.Lamp];

    protected override string KeyColumn(string culture) => culture == "es" ? "Pedido" : "Order";

    protected override string KeyOf(OrderLineForTheBooks row) => row.OrderId.Replace("-", "", StringComparison.Ordinal)[^8..];

    protected override string MoneyColumn(string culture) => culture == "es" ? "Importe" : "Amount";

    protected override Money MoneyOf(OrderLineForTheBooks row) => row.Amount!.Value;

    protected override string TextColumn(string culture) => culture == "es" ? "Descripción" : "Description";

    protected override OrderLineForTheBooks WithText(OrderLineForTheBooks row, string text) => row with { Description = text };

    protected override char SeparatorFor(string culture) => culture == "es" ? ';' : ',';

    protected override char DecimalMarkFor(string culture) => culture == "es" ? ',' : '.';
}

/// <summary>
/// Orders as an accountant opens them (E-4; decision 4): the spreadsheet's own
/// separator and decimal mark for the reader's language, headers and kinds in it,
/// money as numbers it sums — a discount as a negative amount, so a column's sum is
/// what the orders took — and a tax row per rate with its base, beside the lines.
/// </summary>
public sealed class OrdersForTheBooksCsvTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Money Eur(decimal amount) => new(amount, "EUR");

    internal static readonly OrderLineForTheBooks Moka = new(
        "0192f1a2-6c4e-7d3b-9a10-5e8f2b7c41d9", new DateOnly(2026, 10, 2), "Delivered", "Lucía Martín", "ES", BookLineKind.Line,
        "B11COF0401-DEFAULT", "Cafetera italiana", 2, Eur(29.90m), Eur(59.80m), null, null, null);

    internal static readonly OrderLineForTheBooks Lamp = new(
        "0192f1b7-1a2b-7c3d-8e4f-90a1b2c3d4e5", new DateOnly(2026, 10, 3), "Confirmed", "Jordi Puig", "ES", BookLineKind.Line,
        "B12LAM0601-DEFAULT", "Flexo LED articulado", 1, Eur(1074.90m), Eur(1074.90m), null, null, null);

    private static readonly OrderLineForTheBooks Discount = Moka with
    {
        Kind = BookLineKind.Discount,
        Sku = null,
        Description = "WELCOME5 Bienvenida",
        Quantity = null,
        UnitPrice = null,
        Amount = Eur(-5m),
    };

    private static readonly OrderLineForTheBooks Tax = Moka with
    {
        Kind = BookLineKind.Tax,
        Sku = null,
        Description = "standard",
        Quantity = null,
        UnitPrice = null,
        Amount = null,
        TaxRate = 0.21m,
        TaxBase = Eur(45.29m),
        Tax = Eur(9.51m),
    };

    private static async Task<List<IReadOnlyList<string>>> Written(string culture, params OrderLineForTheBooks[] rows)
    {
        using var output = new MemoryStream();
        await new OrdersForTheBooksCsv().WriteAsync(rows, new ExportWriting(culture), output, Ct);
        return [.. Csv.Read(output.ToArray(), culture == "es" ? ';' : ',')];
    }

    [Fact]
    public async Task A_spanish_accountant_reads_spanish_headers_and_kinds_and_decimal_commas()
    {
        var file = await Written("es", Moka, Discount, Tax);

        Assert.Equal(
            ["Pedido", "Día", "Estado", "Cliente", "País", "Concepto", "SKU", "Descripción", "Cantidad", "Precio unitario", "Importe", "Tipo impositivo (%)", "Base imponible", "Impuesto", "Moneda"],
            file[0]);
        Assert.Equal(["2b7c41d9", "2026-10-02", "Entregado", "Lucía Martín", "ES", "línea", "B11COF0401-DEFAULT", "Cafetera italiana", "2", "29,90", "59,80", "", "", "", "EUR"], file[1]);
        Assert.Equal(["descuento", "-5,00"], [file[2][5], file[2][10]]);
        Assert.Equal(["impuesto", "", "21", "45,29", "9,51"], [file[3][5], file[3][10], file[3][11], file[3][12], file[3][13]]);
    }

    [Fact]
    public async Task An_english_accountant_reads_english_and_decimal_points()
    {
        var file = await Written("en", Lamp, Discount);

        Assert.Equal(
            ["Order", "Day", "Status", "Customer", "Country", "Kind", "SKU", "Description", "Quantity", "Unit price", "Amount", "Tax rate (%)", "Tax base", "Tax", "Currency"],
            file[0]);
        Assert.Equal(["Confirmed", "line", "1074.90"], [file[1][2], file[1][5], file[1][10]]);
        Assert.Equal(["discount", "-5.00"], [file[2][5], file[2][10]]);
    }

    /// <summary>A rate of a fraction of a percent keeps it; a whole one is written whole.</summary>
    [Fact]
    public async Task A_rate_is_a_percentage_the_spreadsheet_reads_as_a_number()
    {
        var file = await Written("es", Tax with { TaxRate = 0.055m });

        Assert.Equal("5,5", file[1][11]);
    }

    // The tests below were found by the exports' Stryker close: each mutant they name survived the suite above.

    /// <summary>Every status a sale can be in, in the reader's words; another goes by its code.</summary>
    [Theory]
    [InlineData("PaymentAuthorized", "Pago autorizado", "Payment authorised")]
    [InlineData("Confirmed", "Confirmado", "Confirmed")]
    [InlineData("Shipped", "Enviado", "Shipped")]
    [InlineData("Delivered", "Entregado", "Delivered")]
    [InlineData("Returned", "Returned", "Returned")]
    public async Task A_status_is_said_in_the_readers_words(string status, string spanish, string english)
    {
        var es = await Written("es", Moka with { Status = status });
        var en = await Written("en", Moka with { Status = status });

        Assert.Equal((spanish, english), (es[1][2], en[1][2]));
    }

    /// <summary>A row with no amount still says its currency: the tax's, or the price's; and what it lacks stays empty.</summary>
    [Fact]
    public async Task A_row_without_an_amount_still_says_its_currency_and_leaves_the_rest_empty()
    {
        var file = await Written("en", Tax, Discount, Moka with { Kind = BookLineKind.Shipping, Sku = null, Quantity = null, UnitPrice = null, Amount = Eur(4.95m) });

        Assert.Equal(["tax", "", "", "", "", "EUR"], [file[1][5], file[1][6], file[1][8], file[1][9], file[1][10], file[1][14]]);
        Assert.Equal(["discount", "", "", "", "EUR"], [file[2][5], file[2][6], file[2][8], file[2][9], file[2][14]]);
        Assert.Equal("shipping", file[3][5]);

        var spanish = await Written("es", Moka with { Kind = BookLineKind.Shipping });
        Assert.Equal("envío", spanish[1][5]);
    }

    /// <summary>An id short enough is its own reference, and a dash in it is not.</summary>
    [Theory]
    [InlineData("abcd-efgh", "abcdefgh")]
    [InlineData("abc-def", "abcdef")]
    public async Task A_reference_is_the_ids_last_eight_characters_without_its_dashes(string id, string reference)
    {
        var file = await Written("es", Moka with { OrderId = id });

        Assert.Equal(reference, file[1][0]);
    }

    [Fact]
    public void The_format_is_a_spreadsheet()
    {
        var format = new OrdersForTheBooksCsv();

        Assert.Equal(("csv", "text/csv; charset=utf-8", "csv"), (format.Format, format.ContentType, format.Extension));
    }
}
