using System.ComponentModel;
using ElGuerre.Tendero.SharedKernel;

namespace ElGuerre.Tendero.Inventory.Features.StockHealth;

public sealed record StockToRestockArguments(
    [property: Description("A warehouse code, to narrow the shop's list to what it stocks.")] string? Warehouse = null);

public sealed record ToRestock(string? Warehouse, int RestockAt, int Total, IReadOnlyList<SkuToRestock> Skus);

/// <param name="Available">Across the shop.</param>
/// <param name="InWarehouse">In the warehouse asked for, when one was.</param>
public sealed record SkuToRestock(string Sku, int Available, int? InWarehouse, decimal? PerDay, decimal? CoverageDays);

/// <summary>
/// <c>stock_to_restock</c> (phase 12, ADR 0054): every SKU at or below R-7.1's
/// point, with its days of cover. A warehouse the shop does not have goes back to
/// the model as an error naming the ones it has, so it can ask again.
/// </summary>
public sealed class StockToRestockTool(IQueryDispatcher queries) : ReadTool<StockToRestockArguments, ToRestock>(queries)
{
    public override ToolAudience Audience => ToolAudience.Shopkeeper;

    public override string Name => "stock_to_restock";

    public override string Description =>
        "Every SKU that needs restocking across the shop, sold out first, with units left, units sold a day and days of cover; optionally narrowed to one warehouse, with what it holds.";

    protected override async Task<(ToRestock Data, int Rows, int? Total)> ReadAsync(
        StockToRestockArguments arguments, IQueryDispatcher queries, CancellationToken cancellationToken)
    {
        var list = await queries.SendAsync(new GetRestockListQuery(arguments.Warehouse, ReadLimits.MaxRows), cancellationToken);
        if (list.UnknownWarehouse)
            throw new ArgumentException(
                $"The shop has no warehouse '{arguments.Warehouse}'. Its warehouses are: {string.Join(", ", list.Warehouses)}.");

        var data = new ToRestock(list.Warehouse, list.RestockAt, list.Total,
            [.. list.Rows.Select(row => new SkuToRestock(row.Sku, row.Available, row.InWarehouse, row.PerDay, row.CoverageDays))]);
        return (data, data.Skus.Count, list.Total);
    }

    protected override IReadOnlyList<ResultTable> Tables(ToRestock data) =>
    [
        ResultTable.Of("stockToRestock",
            [
                new("sku", CellKind.Text), new("available", CellKind.Number),
                .. data.Warehouse is null ? Array.Empty<ResultColumn>() : [new ResultColumn("inWarehouse", CellKind.Number)],
                new("perDay", CellKind.Number), new("coverageDays", CellKind.Days),
            ],
            data.Skus.Select(row => (IReadOnlyList<object?>)
            [
                row.Sku, row.Available,
                .. data.Warehouse is null ? Array.Empty<object?>() : [row.InWarehouse],
                row.PerDay, row.CoverageDays,
            ])),
    ];
}
