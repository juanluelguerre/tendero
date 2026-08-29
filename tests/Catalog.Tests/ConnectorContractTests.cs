using Tendero.Catalog.Connectors;
using Tendero.Catalog.Connectors.Seed;
using Xunit;

namespace Tendero.Catalog.Tests.Connectors;

/// <summary>
/// Contrato ejecutable del puerto: TODA implementación (Seed, Shopify, Medusa...)
/// hereda de esta clase y debe pasar la misma suite. Si un conector nuevo
/// rompe alguna regla, se sabe antes de tocar el dominio.
/// Mismo patrón de clases base abstractas que usas en NEX.
/// </summary>
public abstract class CatalogSourceConnectorContractTests
{
    protected abstract ICatalogSourceConnector CreateConnector();

    /// <summary>Los conectores contra sandbox externos pueden relajar esto a >= 1.</summary>
    protected virtual int MinimumExpectedProducts => 1;

    [Fact]
    public void Source_is_a_stable_lowercase_key()
    {
        var connector = CreateConnector();

        Assert.False(string.IsNullOrWhiteSpace(connector.Source));
        Assert.Equal(connector.Source, connector.Source.ToLowerInvariant());
        Assert.DoesNotContain(' ', connector.Source);
    }

    [Fact]
    public async Task Streams_the_expected_minimum_of_products()
    {
        var count = 0;
        await foreach (var _ in CreateConnector().StreamProductsAsync(TestContext.Current.CancellationToken))
            count++;

        Assert.True(count >= MinimumExpectedProducts,
            $"Expected at least {MinimumExpectedProducts} products, got {count}.");
    }

    [Fact]
    public async Task External_ids_are_unique_and_non_empty()
    {
        var seen = new HashSet<string>();

        await foreach (var product in CreateConnector().StreamProductsAsync(TestContext.Current.CancellationToken))
        {
            Assert.False(string.IsNullOrWhiteSpace(product.ExternalId));
            Assert.True(seen.Add(product.ExternalId),
                $"Duplicated external id '{product.ExternalId}'.");
        }
    }

    [Fact]
    public async Task Every_product_has_localized_name_and_valid_price()
    {
        await foreach (var product in CreateConnector().StreamProductsAsync(TestContext.Current.CancellationToken))
        {
            Assert.True(product.Names.Count > 0, $"No name translations in '{product.ExternalId}'.");
            Assert.All(product.Names, kv =>
            {
                Assert.Matches("^[a-z]{2}$", kv.Key); // ISO 639-1: "es", "en"
                Assert.False(string.IsNullOrWhiteSpace(kv.Value));
            });
            Assert.True(product.PriceAmount >= 0, $"Negative price in '{product.ExternalId}'.");
            Assert.Equal(3, product.PriceCurrency.Length); // ISO 4217
        }
    }

    [Fact]
    public async Task Image_urls_are_absolute()
    {
        await foreach (var product in CreateConnector().StreamProductsAsync(TestContext.Current.CancellationToken))
            foreach (var url in product.ImageUrls)
                Assert.True(url.IsAbsoluteUri, $"Relative image url in '{product.ExternalId}'.");
    }
}

/// <summary>La implementación concreta para Seed queda en tres líneas.</summary>
public sealed class SeedCatalogConnectorContractTests : CatalogSourceConnectorContractTests
{
    protected override ICatalogSourceConnector CreateConnector() =>
        new SeedCatalogConnector(
            Microsoft.Extensions.Options.Options.Create(
                new SeedConnectorOptions { FilePath = "TestData/products.sample.json" }));

    protected override int MinimumExpectedProducts => 6;
}
