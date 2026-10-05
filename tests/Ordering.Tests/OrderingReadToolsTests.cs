using System.Text.Json;
using ElGuerre.Tendero.Ordering.Domain;
using ElGuerre.Tendero.Ordering.Features.Dashboard;
using ElGuerre.Tendero.SharedKernel;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ElGuerre.Tendero.Ordering.Tests;

/// <summary>What Steward may read of the shop's sales and returns (S-3b): three tools Ordering owns.</summary>
public sealed class OrderingReadToolsTests
{
    /// <summary>Answers each query with a fixed figure and remembers what was asked.</summary>
    private sealed class FixedQueries : IQueryDispatcher
    {
        public List<object> Asked { get; } = [];

        public Task<TResult> SendAsync<TResult>(IQuery<TResult> query, CancellationToken cancellationToken = default)
        {
            Asked.Add(query);
            object answer = query switch
            {
                GetSalesSummaryQuery sales => new SalesSummary(sales.Period, "EUR",
                    new SalesFigures(120m, 3, 6, 40m, 2m), new SalesFigures(60m, 2, 3, 30m, 1.5m),
                    [new BestSeller("MUG-1", "Mug", 4, 40m)]),
                GetSalesMixQuery mix => new SalesMix(mix.Period, "EUR", [], new ChannelSales(null, 0, 0m),
                    [new CategorySales("MUGS", 4, 40m), new CategorySales(null, 2, 80m)]),
                GetProductPerformanceQuery performance => new ProductPerformance(performance.Period, performance.Ranking,
                    performance.Category, "EUR", 7, [new ProductSales("p-1", "Mug", 4, 40m)]),
                // Every reason is in the query's answer, zero included; a model is given only those with units.
                GetReturnsSummaryQuery returns => new ReturnsSummary(returns.Period, 2, 3,
                    [new ReasonUnits(ReturnReason.WrongSize, 0), new ReasonUnits(ReturnReason.Damaged, 3)], 1,
                    [new SkuReturns("MUG-1", 3, [new ReasonUnits(ReturnReason.WrongSize, 0), new ReasonUnits(ReturnReason.Damaged, 3)])]),
                _ => throw new NotSupportedException(query.GetType().Name),
            };
            return Task.FromResult((TResult)answer);
        }
    }

    private static (IReadOnlyList<IReadTool> Tools, FixedQueries Queries) Compose()
    {
        var queries = new FixedQueries();
        var services = new ServiceCollection();
        services.AddOrdering(new ConfigurationBuilder().Build());
        services.AddSingleton<IQueryDispatcher>(queries);
        return ([.. services.BuildServiceProvider().CreateScope().ServiceProvider.GetServices<IReadTool>()], queries);
    }

    private static JsonElement Arguments(string json) => JsonDocument.Parse(json).RootElement.Clone();

    /// <summary>A table as the panel reads it: its key, and each column's key and kind. The
    /// panel's headings are translations of these names, so a name changed here alone is a
    /// heading that reads as a key.</summary>
    private static string Shape(ResultTable table) =>
        $"{table.Key}: {string.Join(", ", table.Columns.Select(column => $"{column.Key} {column.Kind}"))}";

    [Fact]
    public async Task Each_tools_tables_are_named_as_the_panel_reads_them()
    {
        var (tools, _) = Compose();
        async Task<string[]> Shapes(string tool, string arguments) =>
            [.. (await tools.Single(candidate => candidate.Name == tool).ReadAsync(Arguments(arguments), TestContext.Current.CancellationToken)).Tables!.Select(Shape)];

        Assert.Equal(
        [
            "salesFigures: period Term, revenue Money, orders Number, units Number, averageOrder Money",
            "salesByCategory: category Text, units Number, revenue Money",
            "bestSellers: sku Text, name Text, units Number, revenue Money",
        ], await Shapes("sales_summary", "{}"));
        Assert.Equal(["productPerformance: name Text, units Number, revenue Money"], await Shapes("product_performance", "{}"));
        Assert.Equal(["returnsByReason: reason Term, units Number", "returnsBySku: sku Text, units Number"], await Shapes("returns_summary", "{}"));
    }

    /// <summary>The two periods are rows the panel names: "current" and "previous" are terms it translates.</summary>
    [Fact]
    public async Task The_sales_figures_are_this_period_and_the_one_before_as_terms()
    {
        var (tools, _) = Compose();

        var answer = await tools.Single(tool => tool.Name == "sales_summary").ReadAsync(Arguments("{}"), TestContext.Current.CancellationToken);

        Assert.Equal(["current", "previous"], answer.Tables![0].Rows.Select(row => row[0].Text));
        Assert.Equal([3m, 2m], answer.Tables[0].Rows.Select(row => row[2].Number!.Value));
        Assert.Equal(["MUGS", "uncategorised"], answer.Tables[1].Rows.Select(row => row[0].Text));
    }

    [Fact]
    public void Ordering_registers_its_three_tools()
    {
        Assert.Equal(["product_performance", "returns_summary", "sales_summary"], Compose().Tools.Select(tool => tool.Name).Order());
    }

    [Fact]
    public async Task Sales_summary_is_the_period_against_the_one_before_with_the_mix_by_category()
    {
        var (tools, queries) = Compose();

        var answer = await tools.Single(tool => tool.Name == "sales_summary").ReadAsync(Arguments("""{"period":"Month"}"""), TestContext.Current.CancellationToken);

        var sales = (SalesNow)answer.Data;
        Assert.Equal(("Month", 120m, 60m), (sales.Period, sales.Current.Revenue, sales.Previous.Revenue));
        Assert.Equal([("MUGS", 40m), ("uncategorised", 80m)], sales.ByCategory.Select(row => (row.Category, row.Revenue)));
        Assert.Equal("MUG-1", Assert.Single(sales.BestSellers).Sku);

        // What the panel draws is what was read: the period against the one before, the mix, the best sellers.
        Assert.Equal(["salesFigures", "salesByCategory", "bestSellers"], answer.Tables!.Select(table => table.Key));
        Assert.Equal([120m, 60m], answer.Tables![0].Rows.Select(row => row[1].Number!.Value));
        Assert.All(answer.Tables, table => Assert.Equal("EUR", table.Currency));
        Assert.All(queries.Asked, query => Assert.Equal(DashboardPeriod.Month, query switch
        {
            GetSalesSummaryQuery summary => summary.Period,
            GetSalesMixQuery mix => mix.Period,
            _ => default,
        }));
    }

    [Fact]
    public async Task Product_performance_passes_its_ranking_category_and_limit()
    {
        var (tools, queries) = Compose();

        var answer = await tools.Single(tool => tool.Name == "product_performance").ReadAsync(
            Arguments("""{"period":"Week","ranking":"Worst","category":"KITCHEN","limit":5}"""), TestContext.Current.CancellationToken);

        Assert.Equal(new GetProductPerformanceQuery(DashboardPeriod.Week, PerformanceRanking.Worst, "KITCHEN", 5), Assert.Single(queries.Asked));
        Assert.Equal((1, (int?)7), (answer.Rows, answer.Total));
    }

    [Fact]
    public async Task Returns_summary_is_by_reason_and_by_sku()
    {
        var (tools, _) = Compose();

        var answer = await tools.Single(tool => tool.Name == "returns_summary").ReadAsync(Arguments("""{"period":"Day"}"""), TestContext.Current.CancellationToken);

        var returns = (ReturnsNow)answer.Data;
        Assert.Equal(("Damaged", 3), (returns.ByReason.Single().Reason, returns.ByReason.Single().Units));
        Assert.Equal("MUG-1", returns.BySku.Single().Sku);
        // A reason nobody gave is not a row: of five reasons a model would read four zeros as news.
        Assert.Equal("Damaged", returns.BySku.Single().ByReason.Single().Reason);
        Assert.Equal(["Damaged"], answer.Tables![0].Rows.Select(row => row[0].Text));
    }
}
