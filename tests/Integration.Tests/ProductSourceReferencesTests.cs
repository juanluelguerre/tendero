using ElGuerre.Tendero.Catalog.Domain;
using ElGuerre.Tendero.SharedKernel;
using ElGuerre.Tendero.Tests;
using Microsoft.EntityFrameworkCore;
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
}
