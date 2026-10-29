using System.Text;
using Xunit;

namespace ElGuerre.Tendero.Exports.Tests;

/// <summary>
/// The one writer every format uses (E-1): RFC 4180 for a spreadsheet, a feed's
/// own plainer rules for Merchant Center, and a reader that reads both back.
/// </summary>
public sealed class CsvTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<byte[]> Written(CsvDialect dialect, params IReadOnlyList<string>[] records)
    {
        using var output = new MemoryStream();
        await Csv.WriteAsync(output, ["name", "note"], records, dialect, Ct);
        return output.ToArray();
    }

    /// <summary>A cell holding the separator, a quote or a line break is quoted, its quotes doubled; lines end in CRLF.</summary>
    [Fact]
    public async Task A_spreadsheets_cell_is_quoted_only_when_it_must_be()
    {
        var bytes = await Written(new CsvDialect(';', Quoted: true, ByteOrderMark: false), ["Sartén; 26 cm", "dice \"hola\"\nadiós"], ["Taza", "nada"]);

        Assert.Equal("name;note\r\n\"Sartén; 26 cm\";\"dice \"\"hola\"\"\nadiós\"\r\nTaza;nada\r\n", Encoding.UTF8.GetString(bytes));
    }

    /// <summary>Without a BOM, Excel reads UTF-8 as its own code page and shows Ã¡ for á.</summary>
    [Fact]
    public async Task A_spreadsheet_is_told_its_encoding_and_a_feed_is_not()
    {
        var spreadsheet = await Written(CsvDialect.ForSpreadsheet("es"), ["á", "b"]);
        var feed = await Written(CsvDialect.Feed, ["á", "b"]);

        Assert.Equal([0xEF, 0xBB, 0xBF], spreadsheet[..3]);
        Assert.NotEqual(0xEF, feed[0]);
    }

    /// <summary>Decision 4: the separator a Spanish spreadsheet splits on, and an English one.</summary>
    [Theory]
    [InlineData("es", ';')]
    [InlineData("en", ',')]
    public void A_spreadsheet_is_split_as_its_readers_language_splits_it(string culture, char separator) =>
        Assert.Equal(separator, CsvDialect.ForSpreadsheet(culture).Separator);

    /// <summary>A feed quotes nothing (Merchant Center reads quotes as text) and cannot hold a tab or a line break: each becomes a space.</summary>
    [Fact]
    public async Task A_feeds_cell_carries_no_tab_no_line_break_and_no_quoting()
    {
        var bytes = await Written(CsvDialect.Feed, ["Sartén\t26 cm", "dice \"hola\"\r\nadiós"]);

        Assert.Equal("name\tnote\r\nSartén 26 cm\tdice \"hola\" adiós\r\n", Encoding.UTF8.GetString(bytes));
    }

    [Fact]
    public async Task What_is_written_reads_back_as_it_was()
    {
        string[][] records = [["Sartén; 26 cm", "dice \"hola\"\nadiós"], ["", "último"]];
        var bytes = await Written(CsvDialect.ForSpreadsheet("es"), records);

        var read = Csv.Read(bytes, ';');

        Assert.Equal([["name", "note"], .. records], read);
    }

    /// <summary>A quote opens a quoted cell only where the cell begins (RFC 4180); inside an unquoted one, as a feed writes it, it is text.</summary>
    [Fact]
    public void A_quote_inside_an_unquoted_cell_is_text() =>
        Assert.Equal([["dice \"hola\" ya", "b"]], Csv.Read(Encoding.UTF8.GetBytes("dice \"hola\" ya\tb"), '\t'));

    // The tests below were found by the exports' Stryker close: each mutant they name survived the suite above.

    /// <summary>A cell that begins with what must be quoted is quoted too, not only one that holds it further in.</summary>
    [Fact]
    public async Task A_cell_that_begins_with_a_quote_or_the_separator_is_quoted()
    {
        var bytes = await Written(new CsvDialect(';', Quoted: true, ByteOrderMark: false), ["\"dicho\"", ";uno"]);

        Assert.Equal("name;note\r\n\"\"\"dicho\"\"\";\";uno\"\r\n", Encoding.UTF8.GetString(bytes));
    }

    [Fact]
    public void A_file_that_ends_in_a_quoted_cell_reads_it() =>
        Assert.Equal([["a", "b"], ["c", "d e"]], Csv.Read(Encoding.UTF8.GetBytes("a,b\r\nc,\"d e\""), ','));

    /// <summary>A line break on its own, as a file written elsewhere has it, ends a record as CRLF does, and loses nothing before it.</summary>
    [Fact]
    public void A_bare_line_break_ends_a_record()
    {
        Assert.Equal([["a", "b"], ["c", "d"]], Csv.Read(Encoding.UTF8.GetBytes("a,b\nc,d\n"), ','));
        Assert.Equal([["a", "b\r"]], Csv.Read(Encoding.UTF8.GetBytes("a,b\r"), ','));
    }

    /// <summary>A last record of one cell, or one that ends in an empty cell, is still a record.</summary>
    [Fact]
    public void A_last_record_of_one_cell_or_ending_empty_is_kept()
    {
        Assert.Equal([["a"], ["b"]], Csv.Read(Encoding.UTF8.GetBytes("a\r\nb"), ','));
        Assert.Equal([["a", "b"], ["c", ""]], Csv.Read(Encoding.UTF8.GetBytes("a,b\r\nc,"), ','));
    }

    [Fact]
    public void A_file_that_ends_without_a_line_break_still_has_its_last_record() =>
        Assert.Equal([["a", "b"], ["c", "d"]], Csv.Read(Encoding.UTF8.GetBytes("a,b\r\nc,d"), ','));
}
