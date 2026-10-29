using ElGuerre.Tendero.Catalog.Connectors;
using ElGuerre.Tendero.Catalog.Ports;
using ElGuerre.Tendero.Inventory.Ports;
using ElGuerre.Tendero.Tests;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace ElGuerre.Tendero.Shopify.Tests;

/// <summary>
/// What the host composes with <c>AddShopify</c> (P16-8): with a store's
/// credentials, both of Shopify's adapters — the catalogue's source and the
/// stock's — and the two mappings a person wrote, read from their files; without
/// them, nothing at all. Found by phase 16's Stryker close: no test composed it.
/// </summary>
public sealed class ShopifyRegistrationTests
{
    [Fact]
    public void A_configured_store_is_both_sources_with_its_mappings_read_from_their_files()
    {
        var categories = Path.Combine(Path.GetTempPath(), $"categories-{Guid.NewGuid():N}.json");
        var locations = Path.Combine(Path.GetTempPath(), $"locations-{Guid.NewGuid():N}.json");
        File.WriteAllText(categories, """{ "gid://shopify/TaxonomyCategory/hg-11-1-3": "COFFEE_MAKER" }""");
        File.WriteAllText(locations, """{ "gid://shopify/Location/71": "MAD" }""");
        try
        {
            using var provider = Compose(new()
            {
                ["Shopify:Store"] = FakeShopify.Store,
                ["Shopify:ClientId"] = "client-id",
                ["Shopify:ClientSecret"] = "client-secret",
                ["Shopify:CategoriesFile"] = categories,
                ["Shopify:LocationsFile"] = locations,
            });
            using var scope = provider.CreateScope();

            Assert.Equal("shopify", scope.ServiceProvider.GetRequiredKeyedService<ICatalogSourceConnector>("shopify").Source);
            Assert.Equal("shopify", scope.ServiceProvider.GetRequiredKeyedService<IStockSourceConnector>("shopify").Source);
            Assert.Equal("shopify", scope.ServiceProvider.GetRequiredKeyedService<ElGuerre.Tendero.Accounts.Ports.ICustomerSourceConnector>("shopify").Source);
            Assert.Equal("shopify", scope.ServiceProvider.GetRequiredKeyedService<ElGuerre.Tendero.Ordering.Ports.IOrderSourceConnector>("shopify").Source);
            Assert.Equal("shopify", scope.ServiceProvider.GetRequiredKeyedService<ElGuerre.Tendero.Pricing.Ports.IDiscountSourceConnector>("shopify").Source);
            Assert.Equal("shopify", scope.ServiceProvider.GetRequiredKeyedService<ElGuerre.Tendero.Ordering.Ports.IGiftCardSourceConnector>("shopify").Source);
            var options = provider.GetRequiredService<IOptions<ShopifyOptions>>().Value;
            Assert.Equal("COFFEE_MAKER", options.Categories["gid://shopify/TaxonomyCategory/hg-11-1-3"]);
            Assert.Equal("MAD", options.Locations["gid://shopify/Location/71"]);
        }
        finally
        {
            File.Delete(categories);
            File.Delete(locations);
        }
    }

    /// <summary>A store with no mapping files is still a source: a category or location nobody mapped is a note.</summary>
    [Fact]
    public void A_store_without_mapping_files_is_still_a_source()
    {
        using var provider = Compose(new()
        {
            ["Shopify:Store"] = FakeShopify.Store,
            ["Shopify:AccessToken"] = "shpat_merchant",
            ["Shopify:LocationsFile"] = "",
        });

        Assert.Empty(provider.GetRequiredService<IOptions<ShopifyOptions>>().Value.Locations);
    }

    [Fact]
    public void Without_a_store_shopify_is_no_source()
    {
        using var provider = Compose([]);
        using var scope = provider.CreateScope();

        Assert.Null(scope.ServiceProvider.GetKeyedService<ICatalogSourceConnector>("shopify"));
        Assert.Null(scope.ServiceProvider.GetKeyedService<IStockSourceConnector>("shopify"));
    }

    private static ServiceProvider Compose(Dictionary<string, string?> settings)
    {
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(new TestClock());
        services.AddSingleton<ISourceVariantSkus, NoSkus>();
        services.AddSingleton<ISourceAddresses, NoAddresses>();
        services.AddShopify(new ConfigurationBuilder().AddInMemoryCollection(settings).Build());
        return services.BuildServiceProvider();
    }

    private sealed class NoSkus : ISourceVariantSkus
    {
        public Task<IReadOnlyDictionary<string, string>> SkusOfAsync(string source, IReadOnlyCollection<string> externalVariantIds, CancellationToken ct) =>
            Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string>());
    }

    private sealed class NoAddresses : ISourceAddresses
    {
        public Task<ProductAddress?> ProductAsync(string handle, CancellationToken ct) => Task.FromResult<ProductAddress?>(null);

        public Task<string?> CategoryAsync(string collectionHandle, CancellationToken ct) => Task.FromResult<string?>(null);
    }
}
