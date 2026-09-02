using ElGuerre.Tendero.Catalog.Connectors;
using ElGuerre.Tendero.Catalog.Connectors.Seed;
using Microsoft.Extensions.Options;
using Xunit;

namespace ElGuerre.Tendero.Catalog.Tests.Connectors;

/// <summary>
/// The port's executable contract: EVERY implementation (Seed, Shopify, Medusa…)
/// inherits this class and has to pass the same suite. If a new connector breaks
/// a rule, it is known before the domain is touched.
/// </summary>
public abstract class CatalogSourceConnectorContractTests
{
    protected abstract ICatalogSourceConnector CreateConnector();

    /// <summary>Connectors against external sandboxes may relax this to >= 1.</summary>
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
        // A source may serve its images over HTTP (Shopify from its CDN) or have
        // them on disk (the seed connector, and phase 4's PDF scanning). What the
        // contract demands is that the location be RESOLVABLE without ambient
        // context: the reader does not know what directory anybody launched from.
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

/// <summary>The concrete implementation for Seed takes three lines.</summary>
public sealed class SeedCatalogConnectorContractTests : CatalogSourceConnectorContractTests
{
    protected override ICatalogSourceConnector CreateConnector() =>
        new SeedCatalogConnector(
            Options.Create(
                new SeedConnectorOptions { FilePath = "TestData/products.sample.json" }));

    protected override int MinimumExpectedProducts => 6;
}
