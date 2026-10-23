using ElGuerre.Tendero.Catalog.Domain;
using ElGuerre.Tendero.Catalog.Ports;
using ElGuerre.Tendero.Persistence;
using ElGuerre.Tendero.SharedKernel;
using ElGuerre.Tendero.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ElGuerre.Tendero.Integration.Tests;

/// <summary>
/// What a source knows a product and its variants by, against a real Postgres
/// (phase 16, P16-2): the store's handle beside the product's id, and the
/// store's id for each variant in <c>variant_external_references</c>. The
/// handle changes in place on the same row — EF tracks an owned row by its key,
/// so a new instance under that key would be a second tracked entity — and a
/// variant linked twice is still linked once.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ProductSourceReferencesTests(PostgresFixture postgres)
{
    private static readonly TestClock Clock = new();
    private static readonly Money Price = new(29.90m, "EUR");

    [Fact]
    [Trait("Category", "Integration")]
    public async Task A_products_handle_and_its_variants_source_ids_round_trip_and_follow_the_source()
    {
        postgres.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var factory = await postgres.CreateDatabaseAsync("catalog_source_references");
        await using (var migrate = factory.Create())
            await migrate.Database.MigrateAsync(ct);

        var product = Product.Create(Clock, LocalizedText.From("es", "Cafetera").With("en", "Coffee maker"), Price);
        product.LinkExternal(Clock, "shopify", "8001", "moka-coffee-maker");
        product.AddVariant(Clock, "MOKA-OLV", Price);
        product.LinkVariantExternal(Clock, "MOKA-OLV", "shopify", "44001");
        product.SetVariantBarcode(Clock, "MOKA-OLV", "8400000000011");

        await using (var context = factory.Create())
        {
            context.Products.Add(product);
            await context.SaveChangesAsync(ct);
        }

        await using (var context = factory.Create())
        {
            var read = await context.Products.SingleAsync(each => each.Id == product.Id, ct);
            read.LinkExternal(Clock, "shopify", "8001", "moka-coffee-maker-olive");
            read.LinkVariantExternal(Clock, "MOKA-OLV", "shopify", "44001");
            read.SetVariantBarcode(Clock, "MOKA-OLV", "8400000000035");
            await context.SaveChangesAsync(ct);
        }

        await using (var context = factory.Create())
        {
            var read = await context.Products.SingleAsync(each => each.Id == product.Id, ct);
            var reference = Assert.Single(read.ExternalReferences);
            Assert.Equal(("shopify", "8001", "moka-coffee-maker-olive"), (reference.Source, reference.ExternalId, reference.Handle));

            var variant = Assert.Single(read.Variants);
            Assert.Equal("8400000000035", variant.Barcode);
            Assert.Equal([new ExternalReference("shopify", "44001")], variant.ExternalReferences);
            Assert.Same(variant, read.VariantFromSource("shopify", "44001"));
        }
    }

    /// <summary>
    /// A source's stock names its own variants; the catalogue says which SKU
    /// each is sold under here (S2a, P16-10), so Inventory counts by SKU without
    /// knowing the catalogue. A variant of another source, or one never imported,
    /// is not answered.
    /// </summary>
    [Fact]
    [Trait("Category", "Integration")]
    public async Task A_sources_variants_are_answered_with_the_skus_they_are_sold_under_here()
    {
        postgres.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var factory = await postgres.CreateDatabaseAsync("catalog_source_variant_skus");
        await using (var migrate = factory.Create())
            await migrate.Database.MigrateAsync(ct);

        var product = Product.Create(Clock, LocalizedText.From("es", "Cafetera").With("en", "Coffee maker"), Price);
        product.LinkExternal(Clock, "shopify", "8001");
        product.AddVariant(Clock, "MOKA-OLV", Price);
        product.LinkVariantExternal(Clock, "MOKA-OLV", "shopify", "44001");
        product.AddVariant(Clock, "8001-44002", Price);
        product.LinkVariantExternal(Clock, "8001-44002", "shopify", "44002");
        product.AddVariant(Clock, "SEED-ONLY", Price);
        product.LinkVariantExternal(Clock, "SEED-ONLY", "medusa", "44003");

        await using (var context = factory.Create())
        {
            context.Products.Add(product);
            await context.SaveChangesAsync(ct);
        }

        var services = new ServiceCollection();
        services.AddTenderoPersistence(factory.ConnectionString);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();

        var skus = await scope.ServiceProvider.GetRequiredService<ISourceVariantSkus>()
            .SkusOfAsync("shopify", ["44001", "44002", "44003", "99999"], ct);

        Assert.Equal(new Dictionary<string, string> { ["44001"] = "MOKA-OLV", ["44002"] = "8001-44002" }, skus);
    }
}
