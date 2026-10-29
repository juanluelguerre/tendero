using ElGuerre.Tendero.Catalog.Connectors;
using ElGuerre.Tendero.Catalog.Connectors.Seed;
using Microsoft.Extensions.Options;

namespace ElGuerre.Tendero.Catalog.Tests.Connectors;

/// <summary>The concrete implementation for Seed takes three lines.</summary>
public sealed class SeedCatalogConnectorContractTests : CatalogSourceConnectorContractTests
{
    protected override ICatalogSourceConnector CreateConnector() =>
        new SeedCatalogConnector(
            Options.Create(
                new SeedConnectorOptions { FilePath = "TestData/products.sample.json" }));

    protected override int MinimumExpectedProducts => 6;
}
