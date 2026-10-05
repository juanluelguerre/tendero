using System.ComponentModel;
using ElGuerre.Tendero.SharedKernel;

namespace ElGuerre.Tendero.Ordering.Features.Dashboard;

// What Steward may read of the shop's sales and returns (phase 12, ADR 0054): three
// tools Ordering owns, each mapping its queries into a small DTO of its own —
// figures, SKUs and product names, never a customer or an order.

public sealed record PeriodArguments(
    [property: Description("Day, Week or Month: the period ending now.")] DashboardPeriod Period = DashboardPeriod.Week);

public sealed record SalesNow(
    string Period, string Currency, Figures Current, Figures Previous, IReadOnlyList<CategoryRevenue> ByCategory, IReadOnlyList<TopSeller> BestSellers);

public sealed record Figures(decimal Revenue, int Orders, int Units, decimal AverageOrderValue);

/// <param name="Category">A category code, or "uncategorised" for lines sold before categories were recorded.</param>
public sealed record CategoryRevenue(string Category, int Units, decimal Revenue);

public sealed record TopSeller(string Sku, string Name, int Units, decimal Revenue);

/// <summary><c>sales_summary</c>: the period against the one before, with this period's mix by category.</summary>
public sealed class SalesSummaryTool(IQueryDispatcher queries) : ReadTool<PeriodArguments, SalesNow>(queries)
{
    public override string Name => "sales_summary";

    public override string Description =>
        "Sales in a period ending now (Day, Week or Month) against the period before: revenue, orders, units, average order; revenue by category this period; and the five best-selling SKUs.";

    protected override async Task<(SalesNow Data, int Rows, int? Total)> ReadAsync(
        PeriodArguments arguments, IQueryDispatcher queries, CancellationToken cancellationToken)
    {
        var summary = await queries.SendAsync(new GetSalesSummaryQuery(arguments.Period), cancellationToken);
        var mix = await queries.SendAsync(new GetSalesMixQuery(arguments.Period), cancellationToken);

        var data = new SalesNow(
            arguments.Period.ToString(),
            summary.Currency,
            Of(summary.Current),
            Of(summary.Previous),
            [.. mix.ByCategory.Select(row => new CategoryRevenue(row.CategoryCode ?? "uncategorised", row.Units, row.Revenue))],
            [.. summary.BestSellers.Select(seller => new TopSeller(seller.Sku, seller.Name, seller.Units, seller.Revenue))]);
        return (data, data.ByCategory.Count + data.BestSellers.Count, null);
    }

    private static Figures Of(SalesFigures figures) =>
        new(figures.Revenue, figures.Orders, figures.Units, figures.AverageOrderValue);

    protected override IReadOnlyList<ResultTable> Tables(SalesNow data) =>
    [
        ResultTable.Of("salesFigures",
            [new("period", CellKind.Term), new("revenue", CellKind.Money), new("orders", CellKind.Number),
                new("units", CellKind.Number), new("averageOrder", CellKind.Money)],
            [
                ["current", data.Current.Revenue, data.Current.Orders, data.Current.Units, data.Current.AverageOrderValue],
                ["previous", data.Previous.Revenue, data.Previous.Orders, data.Previous.Units, data.Previous.AverageOrderValue],
            ],
            data.Currency),
        ResultTable.Of("salesByCategory",
            [new("category", CellKind.Text), new("units", CellKind.Number), new("revenue", CellKind.Money)],
            data.ByCategory.Select(row => (IReadOnlyList<object?>)[row.Category, row.Units, row.Revenue]),
            data.Currency),
        ResultTable.Of("bestSellers",
            [new("sku", CellKind.Text), new("name", CellKind.Text), new("units", CellKind.Number), new("revenue", CellKind.Money)],
            data.BestSellers.Select(row => (IReadOnlyList<object?>)[row.Sku, row.Name, row.Units, row.Revenue]),
            data.Currency),
    ];
}

public sealed record PerformanceArguments(
    [property: Description("Day, Week or Month: the period ending now.")] DashboardPeriod Period = DashboardPeriod.Week,
    [property: Description("Best: what sold most. Worst: what sold least, among what sold.")] PerformanceRanking Ranking = PerformanceRanking.Best,
    [property: Description("A category code; its subcategories count too.")] string? Category = null,
    [property: Description("How many products, 1 to 50.")] int Limit = 10);

public sealed record ProductsRanked(string Period, string Ranking, string? Category, string Currency, int Total, IReadOnlyList<RankedSales> Products);

public sealed record RankedSales(string ProductId, string Name, int Units, decimal Revenue);

/// <summary><c>product_performance</c>: the products that sell most or least.</summary>
public sealed class ProductPerformanceTool(IQueryDispatcher queries) : ReadTool<PerformanceArguments, ProductsRanked>(queries)
{
    public override string Name => "product_performance";

    public override string Description =>
        "The products that sold most (Best) or least (Worst) in a period ending now, by units then revenue, optionally in a category and its subcategories. Products that sold nothing are not in it.";

    protected override async Task<(ProductsRanked Data, int Rows, int? Total)> ReadAsync(
        PerformanceArguments arguments, IQueryDispatcher queries, CancellationToken cancellationToken)
    {
        var ranked = await queries.SendAsync(
            new GetProductPerformanceQuery(arguments.Period, arguments.Ranking, arguments.Category, arguments.Limit), cancellationToken);

        var data = new ProductsRanked(ranked.Period.ToString(), ranked.Ranking.ToString(), ranked.Category, ranked.Currency, ranked.Total,
            [.. ranked.Rows.Select(row => new RankedSales(row.ProductId, row.Name, row.Units, row.Revenue))]);
        return (data, data.Products.Count, ranked.Total);
    }

    protected override IReadOnlyList<ResultTable> Tables(ProductsRanked data) =>
    [
        ResultTable.Of("productPerformance",
            [new("name", CellKind.Text), new("units", CellKind.Number), new("revenue", CellKind.Money)],
            data.Products.Select(row => (IReadOnlyList<object?>)[row.Name, row.Units, row.Revenue]),
            data.Currency),
    ];
}

public sealed record ReturnsNow(string Period, int Returns, int Units, IReadOnlyList<ReasonReturned> ByReason, int SkuTotal, IReadOnlyList<SkuReturned> BySku);

public sealed record ReasonReturned(string Reason, int Units);

public sealed record SkuReturned(string Sku, int Units, IReadOnlyList<ReasonReturned> ByReason);

/// <summary><c>returns_summary</c>: returns opened in a period, by reason and by SKU — nobody's words in it.</summary>
public sealed class ReturnsSummaryTool(IQueryDispatcher queries) : ReadTool<PeriodArguments, ReturnsNow>(queries)
{
    public override string Name => "returns_summary";

    public override string Description =>
        "Returns opened in a period ending now (Day, Week or Month): how many, units by reason, and the most returned SKUs with their reasons.";

    protected override async Task<(ReturnsNow Data, int Rows, int? Total)> ReadAsync(
        PeriodArguments arguments, IQueryDispatcher queries, CancellationToken cancellationToken)
    {
        var summary = await queries.SendAsync(new GetReturnsSummaryQuery(arguments.Period, ReadLimits.MaxRows), cancellationToken);

        var data = new ReturnsNow(arguments.Period.ToString(), summary.Returns, summary.Units,
            [.. summary.ByReason.Where(row => row.Units > 0).Select(Reason)],
            summary.SkuTotal,
            [.. summary.BySku.Select(row => new SkuReturned(row.Sku, row.Units, [.. row.ByReason.Where(reason => reason.Units > 0).Select(Reason)]))]);
        return (data, data.BySku.Count, summary.SkuTotal);
    }

    private static ReasonReturned Reason(ReasonUnits row) => new(row.Reason.ToString(), row.Units);

    protected override IReadOnlyList<ResultTable> Tables(ReturnsNow data) =>
    [
        ResultTable.Of("returnsByReason",
            [new("reason", CellKind.Term), new("units", CellKind.Number)],
            data.ByReason.Select(row => (IReadOnlyList<object?>)[row.Reason, row.Units])),
        ResultTable.Of("returnsBySku",
            [new("sku", CellKind.Text), new("units", CellKind.Number)],
            data.BySku.Select(row => (IReadOnlyList<object?>)[row.Sku, row.Units])),
    ];
}
