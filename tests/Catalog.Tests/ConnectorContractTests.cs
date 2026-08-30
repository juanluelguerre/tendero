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
    public async Task Image_locations_are_absolute_and_readable()
    {
        // El origen puede servir sus imágenes por HTTP (Shopify desde su CDN) o
        // tenerlas en disco (el conector seed, y el escaneo de PDFs de la fase 4).
        // Lo que el contrato exige es que la ubicación sea RESOLUBLE sin contexto
        // ambiental: el lector no sabe desde qué directorio se lanzó nadie.
        await foreach (var product in CreateConnector().StreamProductsAsync(TestContext.Current.CancellationToken))
        {
            foreach (var image in product.Images)
            {
                Assert.True(image.IsAbsoluteUri(),
                    $"Relative image location in '{product.ExternalId}': {image.Location}.");
                Assert.True(image.Location.IsFile || image.Location.Scheme is "http" or "https",
                    $"Unsupported image scheme '{image.Location.Scheme}' in '{product.ExternalId}'.");
            }
        }
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
