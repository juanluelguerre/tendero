using System.Globalization;
using ElGuerre.Tendero.SharedKernel;

namespace ElGuerre.Tendero.Exports.Features.OrdersForTheBooks;

/// <summary>
/// Orders as an accountant opens them in a spreadsheet (E-4; decision 4): the
/// reader's language picks the separator and the decimal mark (<c>;</c> and a comma
/// for Spanish, <c>,</c> and a point for English) and the words — headers, kinds,
/// statuses — and money is written as numbers the spreadsheet sums, never with a
/// thousands separator. The day is ISO, which both languages' spreadsheets read as a
/// date; a rate is a percentage. UTF-8 with a BOM, which Excel needs to read <c>á</c>.
/// </summary>
internal sealed class OrdersForTheBooksCsv : IExportFormat<OrderLineForTheBooks>
{
    public const string Key = "csv";

    private static string[] Spanish =>
        ["Pedido", "Día", "Estado", "Cliente", "País", "Concepto", "SKU", "Descripción", "Cantidad", "Precio unitario", "Importe", "Tipo impositivo (%)", "Base imponible", "Impuesto", "Moneda"];

    private static string[] English =>
        ["Order", "Day", "Status", "Customer", "Country", "Kind", "SKU", "Description", "Quantity", "Unit price", "Amount", "Tax rate (%)", "Tax base", "Tax", "Currency"];

    public string Format => Key;

    public string ContentType => "text/csv; charset=utf-8";

    public string Extension => "csv";

    public Task WriteAsync(IReadOnlyList<OrderLineForTheBooks> rows, ExportWriting writing, Stream output, CancellationToken cancellationToken = default)
    {
        var spanish = writing.Culture == "es";
        var mark = spanish ? ',' : '.';
        string Money(Money? money) => money is { } known ? ExportNumbers.Amount(known, mark) : "";

        return Csv.WriteAsync(
            output,
            spanish ? Spanish : English,
            rows.Select(row => (IReadOnlyList<string>)
            [
                Reference(row.OrderId),
                row.Day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                StatusIn(row.Status, spanish),
                row.Customer,
                row.Country,
                KindIn(row.Kind, spanish),
                row.Sku ?? "",
                row.Description,
                row.Quantity?.ToString(CultureInfo.InvariantCulture) ?? "",
                Money(row.UnitPrice),
                Money(row.Amount),
                row.TaxRate is { } rate ? (rate * 100m).ToString("0.##", CultureInfo.InvariantCulture).Replace('.', mark) : "",
                Money(row.TaxBase),
                Money(row.Tax),
                (row.Amount ?? row.Tax ?? row.UnitPrice)?.Currency ?? "",
            ]),
            CsvDialect.ForSpreadsheet(writing.Culture),
            cancellationToken);
    }

    /// <summary>
    /// An order as the backoffice shows it and finds it: the last eight characters of
    /// its id (the front ends' <c>orderReference</c>), so what the accountant reads is
    /// what the shopkeeper types. The end and not the start: a version-7 id begins
    /// with its time, and orders placed together would read the same.
    /// </summary>
    private static string Reference(string orderId)
    {
        var digits = orderId.Replace("-", "", StringComparison.Ordinal);
        return digits.Length <= 8 ? digits : digits[^8..];
    }

    private static string KindIn(BookLineKind kind, bool spanish) => kind switch
    {
        BookLineKind.Line => spanish ? "línea" : "line",
        BookLineKind.Discount => spanish ? "descuento" : "discount",
        BookLineKind.Shipping => spanish ? "envío" : "shipping",
        BookLineKind.Tax => spanish ? "impuesto" : "tax",
    };

    /// <summary>The statuses a sale can be in, in the reader's words; any other by its code.</summary>
    private static string StatusIn(string status, bool spanish) => (status, spanish) switch
    {
        ("PaymentAuthorized", true) => "Pago autorizado",
        ("PaymentAuthorized", false) => "Payment authorised",
        ("Confirmed", true) => "Confirmado",
        ("Shipped", true) => "Enviado",
        ("Delivered", true) => "Entregado",
        _ => status,
    };
}
