using ElGuerre.Tendero.Catalog.Connectors;
using ElGuerre.Tendero.Catalog.Domain;
using ElGuerre.Tendero.Catalog.Ports;
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
    // ---------- a source that tells its variants apart (phase 13a, I-4d) ----------

    private sealed class Colours
    {
        public static AttributeDefinitions Defined()
        {
            var colour = AttributeDefinition.Define(Clock, "COLOR", ElGuerre.Tendero.SharedKernel.LocalizedText.From("es", "color").With("en", "colour"), AttributeKind.Option, isVariantAxis: true);
            colour.AddAlias(Clock, "color");
            colour.AddOption(Clock, "OLIVE", ElGuerre.Tendero.SharedKernel.LocalizedText.From("es", "oliva").With("en", "olive"));
            colour.AddOption(Clock, "BLACK", ElGuerre.Tendero.SharedKernel.LocalizedText.From("es", "negro").With("en", "black"));
            return new AttributeDefinitions([colour]);
        }
    }

    private static ExternalProduct InTwoColours() => AnExternalProduct() with
    {
        Attributes = new Dictionary<string, string>(),
        Variants =
        [
            new ExternalVariant("OLIVE", new Dictionary<string, string> { ["color"] = "oliva" }, Image: 0),
            new ExternalVariant("BLACK", new Dictionary<string, string> { ["color"] = "negro" }, Image: 1),
        ],
    };

    /// <summary>
    /// Each way the source sells it is a variant, placed on the axis its own
    /// word resolves to — the attribute's code and the option's — with a SKU
    /// that is the same on every import. And no default one: the product is not
    /// bought one way.
    /// </summary>
    [Fact]
    public void A_source_that_tells_two_colours_apart_gets_a_variant_of_each_and_no_default()
    {
        var external = InTwoColours();

        var product = external.ToNewProduct("seed", Clock, Colours.Defined());

        Assert.Equal(["COLOR"], product.VariantAxes);
        Assert.Equal(
            [("B073WXYZ01-OLIVE", "OLIVE"), ("B073WXYZ01-BLACK", "BLACK")],
            product.Variants.Select(variant => (variant.Sku, variant.AxisValues["COLOR"])));
        Assert.All(product.Variants, variant => Assert.Equal((external.Price, VariantStatus.Available), (variant.Price, variant.Status)));
    }

    [Fact]
    public void Reimporting_adds_no_variant_and_the_price_follows_the_source()
    {
        var product = InTwoColours().ToNewProduct("seed", Clock, Colours.Defined());

        (InTwoColours() with { PriceAmount = 35m }).ApplyTo(product, Clock, Colours.Defined());

        Assert.Equal(2, product.Variants.Count);
        Assert.All(product.Variants, variant => Assert.Equal(35m, variant.Price.Amount));
    }

    /// <summary>
    /// A product imported before the source told its colours apart has a
    /// default variant. The day the source does, that one is retired — an order
    /// placed against it still names its SKU — and the two take its place.
    /// </summary>
    [Fact]
    public void A_source_that_starts_telling_variants_apart_retires_the_default_one()
    {
        var product = AnExternalProduct().ToNewProduct("seed", Clock, Colours.Defined());

        InTwoColours().ApplyTo(product, Clock, Colours.Defined());

        Assert.Equal(
            [("B073WXYZ01-DEFAULT", VariantStatus.Discontinued), ("B073WXYZ01-OLIVE", VariantStatus.Available), ("B073WXYZ01-BLACK", VariantStatus.Available)],
            product.Variants.Select(variant => (variant.Sku, variant.Status)));
    }
}
