using ElGuerre.Tendero.SharedKernel;
using Xunit;

namespace ElGuerre.Tendero.Exports.Tests;

/// <summary>
/// What every format adapter promises, whatever its rows (E-1; spec, *Each adapter
/// passes a contract suite*): every row it is given comes out, in order; money in
/// its currency's decimals, as a number the reader can sum; the file reads back as
/// its format is read, each record as wide as its header; an export of nothing is
/// its header; and a text holding the separator, a quote or a line break comes back
/// as its words. Each format's tests inherit it and say where to look.
/// </summary>
public abstract class ExportFormatContractTests<TRow>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>A text built to break a careless writer: both separators, a tab, quotes and a line break.</summary>
    private const string Hostile = "Sartén; \"26 cm\",\tpara\ninducción";

    protected abstract IExportFormat<TRow> CreateFormat();

    /// <summary>At least two rows, each with its own key, in the order the file must keep; the first line of a row carries its money and its text.</summary>
    protected abstract IReadOnlyList<TRow> Rows();

    /// <summary>The column naming each row; a record with nothing in it (a picture's extra line) names none.</summary>
    protected abstract string KeyColumn(string culture);

    protected abstract string KeyOf(TRow row);

    protected abstract string MoneyColumn(string culture);

    protected abstract Money MoneyOf(TRow row);

    protected abstract string TextColumn(string culture);

    protected abstract TRow WithText(TRow row, string text);

    /// <summary>What splits the cells for a reader of this language.</summary>
    protected abstract char SeparatorFor(string culture);

    /// <summary>The decimal mark the format writes for a reader of this language.</summary>
    protected abstract char DecimalMarkFor(string culture);

    private async Task<List<IReadOnlyList<string>>> WrittenAsync(IReadOnlyList<TRow> rows, string culture)
    {
        using var output = new MemoryStream();
        await CreateFormat().WriteAsync(rows, new ExportWriting(culture), output, Ct);
        return [.. Csv.Read(output.ToArray(), SeparatorFor(culture))];
    }

    private static int Column(IReadOnlyList<string> header, string name)
    {
        var index = header.ToList().IndexOf(name);
        Assert.True(index >= 0, $"the header has no {name}: {string.Join(" | ", header)}");
        return index;
    }

    [Theory]
    [InlineData("es")]
    [InlineData("en")]
    public async Task Every_row_comes_out_in_order(string culture)
    {
        var rows = Rows();
        Assert.True(rows.Count >= 2, "the suite needs two rows to see an order");

        var file = await WrittenAsync(rows, culture);

        // A row may take several lines (a product's variants and pictures), and rows may share a key (an
        // order's lines): a key repeated in a run is one, on both sides.
        var key = Column(file[0], KeyColumn(culture));
        Assert.Equal(Runs(rows.Select(KeyOf)), Runs(file.Skip(1).Select(record => record[key]).Where(cell => cell.Length > 0)));
    }

    private static List<string> Runs(IEnumerable<string> keys)
    {
        var all = keys.ToList();
        return [.. all.Where((key, index) => index == 0 || key != all[index - 1])];
    }

    [Theory]
    [InlineData("es")]
    [InlineData("en")]
    public async Task Every_record_is_as_wide_as_its_header(string culture)
    {
        var file = await WrittenAsync(Rows(), culture);

        Assert.All(file.Skip(1), record => Assert.Equal(file[0].Count, record.Count));
    }

    /// <summary>The cell starts with the amount in the currency's decimals and the reader's mark, and no digit follows it.</summary>
    [Theory]
    [InlineData("es")]
    [InlineData("en")]
    public async Task Money_is_written_in_its_currencys_decimals(string culture)
    {
        var rows = Rows();
        var file = await WrittenAsync(rows, culture);
        var (key, money) = (Column(file[0], KeyColumn(culture)), Column(file[0], MoneyColumn(culture)));

        foreach (var row in rows)
        {
            var cell = file.Skip(1).First(record => record[key] == KeyOf(row))[money];
            var expected = ExportNumbers.Amount(MoneyOf(row), DecimalMarkFor(culture));
            Assert.StartsWith(expected, cell, StringComparison.Ordinal);
            Assert.False(cell.Length > expected.Length && char.IsDigit(cell[expected.Length]), $"«{cell}» has more digits than «{expected}»");
        }
    }

    [Theory]
    [InlineData("es")]
    [InlineData("en")]
    public async Task An_export_of_nothing_is_its_header(string culture)
    {
        var file = await WrittenAsync([], culture);

        var header = Assert.Single(file);
        Assert.Contains(KeyColumn(culture), header);
    }

    [Theory]
    [InlineData("es")]
    [InlineData("en")]
    public async Task A_text_with_separators_quotes_and_a_line_break_comes_back_as_its_words(string culture)
    {
        var rows = Rows().Select((row, index) => index == 0 ? WithText(row, Hostile) : row).ToList();

        var file = await WrittenAsync(rows, culture);

        var record = file.Skip(1).First(each => each[Column(file[0], KeyColumn(culture))] == KeyOf(rows[0]));
        Assert.Equal(Words(Hostile), Words(record[Column(file[0], TextColumn(culture))]));
    }

    [Fact]
    public void The_file_says_what_it_is()
    {
        var format = CreateFormat();

        Assert.Matches("^[a-z][a-z-]*$", format.Format);
        Assert.Contains("charset=utf-8", format.ContentType, StringComparison.Ordinal);
        Assert.Matches("^[a-z]+$", format.Extension);
    }

    private static string Words(string text) => string.Join(' ', text.Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries));
}
