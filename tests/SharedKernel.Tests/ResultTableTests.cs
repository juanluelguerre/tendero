using Xunit;

namespace ElGuerre.Tendero.SharedKernel.Tests;

/// <summary>The table the panel draws from a read's rows (P12-10): built by code, bounded, typed by column.</summary>
public sealed class ResultTableTests
{
    private static readonly ResultColumn[] Two = [new("sku", CellKind.Text), new("units", CellKind.Number)];

    [Fact]
    public void A_string_is_text_a_number_is_a_number_and_null_is_empty()
    {
        var table = ResultTable.Of("t", Two, [["MUG-1", 3], ["MUG-2", null], ["MUG-3", 2.5m]]);

        Assert.Equal(
            [[new ResultCell("MUG-1", null), new ResultCell(null, 3m)],
             [new ResultCell("MUG-2", null), new ResultCell(null, null)],
             [new ResultCell("MUG-3", null), new ResultCell(null, 2.5m)]],
            table.Rows);
    }

    /// <summary>A read is capped at its rows and so is what is drawn of it: the panel never gets more than the model could.</summary>
    [Fact]
    public void A_table_carries_no_more_rows_than_a_read_may()
    {
        var table = ResultTable.Of("t", Two, Enumerable.Range(0, ReadLimits.MaxRows + 10).Select(i => (IReadOnlyList<object?>)[$"SKU-{i}", i]));

        Assert.Equal(ReadLimits.MaxRows, table.Rows.Count);
    }

    [Fact]
    public void The_currency_belongs_to_the_table_for_its_money_columns()
    {
        var table = ResultTable.Of("t", [new("revenue", CellKind.Money)], [[12m]], "EUR");

        Assert.Equal("EUR", table.Currency);
    }
}
