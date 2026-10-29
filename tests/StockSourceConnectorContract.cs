using ElGuerre.Tendero.Inventory.Ports;
using Xunit;

namespace ElGuerre.Tendero.Inventory.Tests.Sources;

/// <summary>
/// The stock port's executable contract (phase 16, S2a): every stock source
/// inherits this class and passes the same suite. Linked as a file into each test
/// project that has a stock source (Inventory.Tests for its fake, Shopify.Tests
/// for Shopify), like the catalogue sources' suite.
/// </summary>
public abstract class StockSourceConnectorContractTests
{
    protected abstract IStockSourceConnector CreateConnector();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void Source_is_a_stable_lowercase_key()
    {
        var connector = CreateConnector();

        Assert.False(string.IsNullOrWhiteSpace(connector.Source));
        Assert.Equal(connector.Source, connector.Source.ToLowerInvariant());
        Assert.DoesNotContain(' ', connector.Source);
    }

    [Fact]
    public async Task Warehouses_have_a_code_and_a_name_and_no_code_twice()
    {
        var codes = new HashSet<string>(StringComparer.Ordinal);
        await foreach (var warehouse in CreateConnector().StreamWarehousesAsync(Ct))
        {
            Assert.False(string.IsNullOrWhiteSpace(warehouse.Code));
            Assert.False(string.IsNullOrWhiteSpace(warehouse.Name));
            Assert.True(codes.Add(warehouse.Code), $"Duplicated warehouse code '{warehouse.Code}'.");
        }
    }

    /// <summary>
    /// A level is a count: a SKU, a warehouse, and how many — never negative, and
    /// one count per SKU and warehouse, or the second would silently overwrite
    /// the first.
    /// </summary>
    [Fact]
    public async Task Levels_are_counts_one_per_sku_and_warehouse()
    {
        var seen = new HashSet<(string, string)>();
        var count = 0;
        await foreach (var level in CreateConnector().StreamLevelsAsync(Ct))
        {
            Assert.False(string.IsNullOrWhiteSpace(level.Sku));
            Assert.False(string.IsNullOrWhiteSpace(level.WarehouseCode));
            Assert.True(level.OnHand >= 0, $"Negative count for {level.Sku} in {level.WarehouseCode}.");
            Assert.True(seen.Add((level.Sku, level.WarehouseCode)), $"Two counts for {level.Sku} in {level.WarehouseCode}.");
            count++;
        }

        Assert.True(count > 0, "A stock source that counts nothing proves nothing.");
    }
}
