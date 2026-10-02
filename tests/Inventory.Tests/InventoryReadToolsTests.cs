using System.Text.Json;
using ElGuerre.Tendero.Inventory.Domain;
using ElGuerre.Tendero.Inventory.Features.StockHealth;
using ElGuerre.Tendero.SharedKernel;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ElGuerre.Tendero.Inventory.Tests;

/// <summary>What Steward may read of the stock (S-3b): one tool Inventory owns.</summary>
public sealed class InventoryReadToolsTests
{
    private sealed class FixedQueries(RestockList answer) : IQueryDispatcher
    {
        public GetRestockListQuery? Asked { get; private set; }

        public Task<TResult> SendAsync<TResult>(IQuery<TResult> query, CancellationToken cancellationToken = default)
        {
            Asked = (GetRestockListQuery)(object)query;
            return Task.FromResult((TResult)(object)answer);
        }
    }

    private static (IReadTool Tool, FixedQueries Queries) Compose(RestockList answer)
    {
        var queries = new FixedQueries(answer);
        var services = new ServiceCollection();
        services.AddInventory(new ConfigurationBuilder().Build());
        services.AddSingleton<IQueryDispatcher>(queries);
        return (services.BuildServiceProvider().CreateScope().ServiceProvider.GetServices<IReadTool>().Single(), queries);
    }

    private static JsonElement Arguments(string json) => JsonDocument.Parse(json).RootElement.Clone();

    [Fact]
    public async Task Stock_to_restock_answers_each_sku_with_its_days_of_cover()
    {
        var (tool, queries) = Compose(new RestockList("MAD", false, ["BCN", "MAD"], Restocking.At, 7, [new RestockRow("CUP", 3, 1, 0.5m, 6m)]));

        var answer = await tool.ReadAsync(Arguments("""{"warehouse":"MAD"}"""), TestContext.Current.CancellationToken);

        Assert.Equal("stock_to_restock", tool.Name);
        Assert.Equal("MAD", queries.Asked!.Warehouse);
        var row = Assert.Single(((ToRestock)answer.Data).Skus);
        Assert.Equal(("CUP", 3, (int?)1, (decimal?)6m), (row.Sku, row.Available, row.InWarehouse, row.CoverageDays));
        Assert.Equal((1, (int?)7), (answer.Rows, answer.Total));
    }

    /// <summary>A warehouse the shop does not have goes back to the model with the
    /// ones it has, so it can ask again — never "nothing to restock".</summary>
    [Fact]
    public async Task A_warehouse_the_shop_does_not_have_is_an_error_naming_the_ones_it_has()
    {
        var (tool, _) = Compose(new RestockList("Madrid", true, ["BCN", "MAD"], Restocking.At, 0, []));

        var failure = await Assert.ThrowsAsync<ArgumentException>(() =>
            tool.ReadAsync(Arguments("""{"warehouse":"Madrid"}"""), TestContext.Current.CancellationToken));

        Assert.Contains("BCN", failure.Message);
        Assert.Contains("MAD", failure.Message);
    }
}
