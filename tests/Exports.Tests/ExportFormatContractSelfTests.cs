using ElGuerre.Tendero.SharedKernel;
using Xunit;

namespace ElGuerre.Tendero.Exports.Tests;

/// <summary>
/// The contract suite run before any real format exists (E-1), against the
/// smallest one that keeps it: two columns for a spreadsheet. A suite nobody has
/// seen pass is a suite nobody knows can.
/// </summary>
public sealed class ExportFormatContractSelfTests : ExportFormatContractTests<ExportFormatContractSelfTests.Line>
{
    public sealed record Line(string Name, Money Price);

    private sealed class TwoColumns : IExportFormat<Line>
    {
        public string Format => "two-columns";

        public string ContentType => "text/csv; charset=utf-8";

        public string Extension => "csv";

        public Task WriteAsync(IReadOnlyList<Line> rows, ExportWriting writing, Stream output, CancellationToken cancellationToken = default) =>
            Csv.WriteAsync(
                output,
                ["name", "price"],
                rows.Select(row => (IReadOnlyList<string>)[row.Name, ExportNumbers.Amount(row.Price, writing.Culture == "es" ? ',' : '.')]),
                CsvDialect.ForSpreadsheet(writing.Culture),
                cancellationToken);
    }

    protected override IExportFormat<Line> CreateFormat() => new TwoColumns();

    protected override IReadOnlyList<Line> Rows() => [new("Taza de gres", new Money(10m, "EUR")), new("Sartén", new Money(1234.5m, "EUR"))];

    protected override string KeyColumn(string culture) => "name";

    protected override string KeyOf(Line row) => row.Name;

    protected override string MoneyColumn(string culture) => "price";

    protected override Money MoneyOf(Line row) => row.Price;

    protected override string TextColumn(string culture) => "name";

    protected override Line WithText(Line row, string text) => row with { Name = text };

    protected override char SeparatorFor(string culture) => CsvDialect.ForSpreadsheet(culture).Separator;

    protected override char DecimalMarkFor(string culture) => culture == "es" ? ',' : '.';
}

/// <summary>Money as a number a reader sums: the currency's decimals, the reader's mark, no thousands, and no rounding here.</summary>
public sealed class ExportNumbersTests
{
    [Theory]
    [InlineData(1234.5, "EUR", '.', "1234.50")]
    [InlineData(1234.5, "EUR", ',', "1234,50")]
    [InlineData(1500, "JPY", ',', "1500")]
    [InlineData(1.25, "KWD", '.', "1.250")]
    public void An_amount_is_its_currencys_decimals_with_the_readers_mark(decimal amount, string currency, char mark, string expected) =>
        Assert.Equal(expected, ExportNumbers.Amount(new Money(amount, currency), mark));

    [Fact]
    public void A_fraction_of_a_cent_is_refused_not_rounded() =>
        Assert.Throws<ArgumentException>(() => ExportNumbers.Amount(new Money(1.005m, "EUR")));
}
