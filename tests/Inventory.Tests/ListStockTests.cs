using ElGuerre.Tendero.Inventory.Domain;
using ElGuerre.Tendero.Inventory.Features.ListStock;
using Xunit;

namespace ElGuerre.Tendero.Inventory.Tests;

/// <summary>
/// The stock screen's filters and search (R-7.1). What is decided here is the
/// server's: when a SKU needs restocking, counted by SKU and summed over the
/// warehouses, and which half of a search matched a row.
/// </summary>
public sealed class ListStockTests
{
    private static async Task<ListStockResult> List(FakeStock stock, ListStockQuery query) =>
        await new ListStockHandler(stock, new FakeWarehouses()).HandleAsync(query, TestContext.Current.CancellationToken);

    /// <summary>
    /// A SKU is judged by its sum and counted once. BAG has three in one warehouse
    /// and four in the other: each row alone is under the line, the SKU is not.
    /// </summary>
    [Fact]
    public async Task Counts_skus_by_what_all_their_warehouses_have()
    {
        var stock = new FakeStock();
        stock.Put("BAG-01", "MAD", 3);
        stock.Put("BAG-01", "BCN", 4);
        stock.Put("CUP-02", "MAD", 2);
        stock.Put("CUP-02", "BCN", 0);
        stock.Put("LAMP-03", "MAD", 0);

        var result = await List(stock, new ListStockQuery("en", StockFilter.Restock));

        Assert.Equal([("CUP-02", "MAD"), ("CUP-02", "BCN"), ("LAMP-03", "MAD")],
            result.Rows.Select(row => (row.Sku, row.WarehouseCode)));
        Assert.Equal(2, CountOf(result, StockFilter.Restock));
        Assert.Equal(1, CountOf(result, StockFilter.SoldOut));
        Assert.Equal(3, CountOf(result, StockFilter.All));
        Assert.Equal(Restocking.At, result.RestockAt);
    }

    [Fact]
    public async Task Sold_out_is_nothing_available_anywhere()
    {
        var stock = new FakeStock();
        stock.Put("CUP-02", "MAD", 2);
        stock.Put("CUP-02", "BCN", 0);
        stock.Put("LAMP-03", "MAD", 0);

        var result = await List(stock, new ListStockQuery("en", StockFilter.SoldOut));

        Assert.Equal("LAMP-03", Assert.Single(result.Rows).Sku);
    }

    /// <summary>
    /// The ledger matches the SKU; the catalogue matched LAMP-03 by its product's
    /// name. Either half shows a row, and the counts are of what was found.
    /// </summary>
    [Fact]
    public async Task A_search_shows_what_the_sku_or_the_catalogue_matched()
    {
        var stock = new FakeStock();
        stock.Put("BAG-01", "MAD", 7);
        stock.Put("CUP-02", "MAD", 2);
        stock.Put("LAMP-03", "MAD", 0);

        var result = await List(stock, new ListStockQuery("en", StockFilter.All, Text: "bag", Skus: ["LAMP-03"]));

        Assert.Equal(["BAG-01", "LAMP-03"], result.Rows.Select(row => row.Sku));
        Assert.Equal(2, CountOf(result, StockFilter.All));
        Assert.Equal(1, CountOf(result, StockFilter.Restock));
    }

    /// <summary>A search that matched no product and no SKU is empty, not everything.</summary>
    [Fact]
    public async Task A_search_the_catalogue_matched_nothing_for_shows_nothing()
    {
        var stock = new FakeStock();
        stock.Put("BAG-01", "MAD", 7);

        var result = await List(stock, new ListStockQuery("en", StockFilter.All, Skus: []));

        Assert.Empty(result.Rows);
        Assert.Equal(0, CountOf(result, StockFilter.All));
    }

    /// <summary>
    /// A page is of SKUs, taken after the filter (R-7.2). Six SKUs put in out of
    /// order, four to restock, and C-03 in two warehouses: a page cut from the
    /// rows would split it, a page cut before the filter would hold A-01, and a
    /// page that ignored its number would serve the first twice.
    /// </summary>
    [Fact]
    public async Task Pages_the_filtered_skus_with_every_warehouse_of_each()
    {
        var stock = new FakeStock();
        stock.Put("E-05", "MAD", 0);
        stock.Put("A-01", "MAD", 9);
        stock.Put("C-03", "MAD", 1);
        stock.Put("C-03", "BCN", 1);
        stock.Put("B-02", "MAD", 3);
        stock.Put("D-04", "MAD", 2);
        stock.Put("D-04", "BCN", 9);
        stock.Put("F-06", "MAD", 4);

        var first = await List(stock, new ListStockQuery("en", StockFilter.Restock, Page: 1, PageSize: 2));
        var second = await List(stock, new ListStockQuery("en", StockFilter.Restock, Page: 2, PageSize: 2));

        Assert.Equal([("B-02", "MAD"), ("C-03", "MAD"), ("C-03", "BCN")], first.Rows.Select(row => (row.Sku, row.WarehouseCode)));
        Assert.Equal([("E-05", "MAD"), ("F-06", "MAD")], second.Rows.Select(row => (row.Sku, row.WarehouseCode)));
        Assert.Equal((4, 2, 2), (second.Total, second.Page, second.PageSize));
        Assert.Equal(CountOf(second, StockFilter.Restock), second.Total);
        Assert.Equal((1, 6), (CountOf(second, StockFilter.SoldOut), CountOf(second, StockFilter.All)));
    }

    [Theory]
    [InlineData(0, 20)]
    [InlineData(1, 0)]
    [InlineData(1, 101)]
    public void A_page_outside_the_contract_is_refused(int page, int pageSize) =>
        Assert.False(new ListStockValidator().Validate(new ListStockQuery("en", Page: page, PageSize: pageSize)).IsValid);

    [Theory]
    [InlineData(1, 1)]
    [InlineData(4, 100)]
    public void A_page_inside_the_contract_is_accepted(int page, int pageSize) =>
        Assert.True(new ListStockValidator().Validate(new ListStockQuery("en", Page: page, PageSize: pageSize)).IsValid);

    private static int CountOf(ListStockResult result, StockFilter filter) =>
        result.Counts.Single(count => count.Filter == filter).Count;
}
