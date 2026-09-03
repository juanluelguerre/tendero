using ElGuerre.Tendero.Catalog.Connectors;
using ElGuerre.Tendero.Catalog.Domain;
using ElGuerre.Tendero.Tests;
using Xunit;

namespace ElGuerre.Tendero.Catalog.Tests.Connectors;

/// <summary>
/// Mapping from source to aggregate has two consumers that cannot call each
/// other: the import slice and the search quality gate. While it was written
/// twice, the only thing guaranteeing they matched was a comment. This is what
/// guarantees it now.
/// </summary>
public sealed class ExternalProductMapperTests
{
    private static readonly TestClock Clock = new();

    /// <summary>
    /// A source that draws no distinction between sizes or colours is describing
    /// a product with a single way of being bought. Calling that a default
    /// variant is more honest than leaving the cart with two paths, one with a
    /// variant and one without.
    /// </summary>
    [Fact]
    public void An_imported_product_always_has_something_to_buy()
    {
        var external = AnExternalProduct();

        var product = external.ToNewProduct("seed", Clock);

        var variant = Assert.Single(product.Variants);
        Assert.Equal($"{external.ExternalId}-DEFAULT", variant.Sku);
        Assert.Equal(external.Price, variant.Price);
    }

    [Fact]
    public void Reimporting_does_not_add_a_second_default_variant()
    {
        var external = AnExternalProduct();
        var product = external.ToNewProduct("seed", Clock);

        external.ApplyTo(product, Clock);

        Assert.Single(product.Variants);
    }

    private static ExternalProduct AnExternalProduct(decimal price = 29.90m) => new(
        ExternalId: "B073WXYZ01",
        Names: new Dictionary<string, string> { ["es"] = "Cafetera", ["en"] = "Coffee maker" },
        Descriptions: new Dictionary<string, string> { ["es"] = "De goteo" },
        Brand: "Moka",
        Category: "COFFEE_MAKER",
        PriceAmount: price,
        PriceCurrency: "EUR",
        Images: [],
        Attributes: new Dictionary<string, string> { ["color"] = "negro" });

    [Fact]
    public void A_new_product_carries_everything_the_source_gave()
    {
        var product = AnExternalProduct().ToNewProduct("seed", Clock);

        Assert.Equal("Cafetera", product.Name.In("es"));
        Assert.Equal("Coffee maker", product.Name.In("en"));
        Assert.Equal("De goteo", product.Description?.In("es"));
        Assert.Equal("Moka", product.Brand);
        Assert.Equal("COFFEE_MAKER", product.Category);
        Assert.Equal(29.90m, product.Price.Amount);
        // With no definitions the attribute falls back to plain text: that is the
        // previous behaviour, and degrading to what was already there beats
        // failing the import because nobody defined the attributes first.
        var colour = product.AttributeFor("COLOR");
        Assert.NotNull(colour);
        Assert.Equal(AttributeKind.Text, colour.Kind);
        Assert.Equal("negro", colour.RawText);
    }

    [Fact]
    public void A_new_product_is_linked_to_its_source_and_left_in_draft()
    {
        // Importing never publishes: Draft is the review queue's reason to exist,
        // and only PublishProduct moves a product to Active (ADR 0012).
        var product = AnExternalProduct().ToNewProduct("seed", Clock);

        Assert.Equal(ProductStatus.Draft, product.Status);
        Assert.Contains(new ExternalReference("seed", "B073WXYZ01"), product.ExternalReferences);
    }

    [Fact]
    public void Reimporting_updates_the_product_without_unpublishing_it()
    {
        // The source rules over what the source owns: texts, price, attributes.
        // Not over the status — a supplier changing a description cannot put
        // something already published back into the review queue.
        var product = AnExternalProduct().ToNewProduct("seed", Clock);
        product.Publish(Clock);

        AnExternalProduct(price: 34.50m).ApplyTo(product, Clock);

        Assert.Equal(34.50m, product.Price.Amount);
        Assert.Equal(ProductStatus.Active, product.Status);
    }

    [Fact]
    public void Reimporting_twice_does_not_duplicate_the_external_reference()
    {
        var external = AnExternalProduct();
        var product = external.ToNewProduct("seed", Clock);

        external.ApplyTo(product, Clock);
        product.LinkExternal(Clock, "seed", external.ExternalId);

        Assert.Single(product.ExternalReferences);
    }
}
