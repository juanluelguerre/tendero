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
            new ExternalVariant("OLIVE", new Dictionary<string, string> { ["color"] = "oliva" }),
            new ExternalVariant("BLACK", new Dictionary<string, string> { ["color"] = "negro" }),
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

    /// <summary>
    /// What a picture shows arrives in the same words a variant's colour does,
    /// and lands on the same value: the attribute's code and the option's. A word
    /// the catalogue has no option for stays as the source wrote it — which no
    /// variant holds, and the aggregate then refuses.
    /// </summary>
    [Fact]
    public void The_sources_words_for_what_a_picture_shows_resolve_as_a_variants_do()
    {
        var black = InTwoColours().ToNewProduct("seed", Clock, Colours.Defined()).Variants.Single(variant => variant.Sku.EndsWith("BLACK", StringComparison.Ordinal));

        var shown = ExternalProductMapper.Coordinates(new Dictionary<string, string> { ["color"] = "negro" }, Colours.Defined());

        Assert.Equal(black.AxisValues, shown);
        Assert.Equal("BLACK", shown["color"]);
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

    /// <summary>
    /// The pannier's own history: imported with a colour of its own, then sold
    /// in three (2026-10-08). The colour the product said goes with the default
    /// variant: from then on its variants say it.
    /// </summary>
    [Fact]
    public void A_source_that_starts_telling_colours_apart_takes_the_colour_off_the_product()
    {
        var product = AnExternalProduct().ToNewProduct("seed", Clock, Colours.Defined());

        InTwoColours().ApplyTo(product, Clock, Colours.Defined());

        Assert.Null(product.AttributeFor("COLOR"));
    }

    /// <summary>
    /// A catalogue imported before the rule still holds the product's colour
    /// beside its variants', with its axes already declared. The next import
    /// takes it off; nothing else moves.
    /// </summary>
    [Fact]
    public void Reimporting_takes_off_a_colour_a_product_kept_from_before_the_rule()
    {
        var product = InTwoColours().ToNewProduct("seed", Clock, Colours.Defined());
        LeftFromBeforeTheRule(product, AttributeValue.Plain("COLOR", "oliva"));

        InTwoColours().ApplyTo(product, Clock, Colours.Defined());

        Assert.Equal(((AttributeValue?)null, 2), (product.AttributeFor("COLOR"), product.Variants.Count));
    }

    /// <summary>A source that says a colour for the product and for each variant too: the variants' is the one kept.</summary>
    [Fact]
    public void A_colour_said_for_the_product_and_for_its_variants_stays_with_the_variants()
    {
        var both = InTwoColours() with { Attributes = new Dictionary<string, string> { ["color"] = "negro" } };
        var product = both.ToNewProduct("seed", Clock, Colours.Defined());

        both.ApplyTo(product, Clock, Colours.Defined());

        Assert.Equal(((AttributeValue?)null, 2), (product.AttributeFor("COLOR"), product.Variants.Count));
    }

    /// <summary>The state the rule cannot produce any more, so it is written past the aggregate, as the database holds it.</summary>
    private static void LeftFromBeforeTheRule(Product product, AttributeValue value)
    {
        var field = typeof(Product).GetField("attributes", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        ((List<AttributeValue>)field.GetValue(product)!).Add(value);
    }

    // ---------- a source whose variants have identities of their own (phase 16, P16-2) ----------

    private static ExternalProduct InTwoColoursFromAStore(
        string? oliveSku = "MOKA-OLV", string? blackSku = "MOKA-BLK", decimal? blackPrice = 32.50m) =>
        AnExternalProduct() with
        {
            Attributes = new Dictionary<string, string>(),
            Handle = "moka-coffee-maker",
            Variants =
            [
                new ExternalVariant("44001", new Dictionary<string, string> { ["color"] = "oliva" },
                    ExternalId: "44001", Sku: oliveSku, Barcode: "8400000000011"),
                new ExternalVariant("44002", new Dictionary<string, string> { ["color"] = "negro" },
                    ExternalId: "44002", Sku: blackSku, Barcode: "8400000000028", PriceAmount: blackPrice),
            ],
        };

    /// <summary>
    /// A merchant who leaves a store keeps the SKUs printed on their labels: a
    /// variant the source gives a SKU is sold under it here (spec decision 1).
    /// </summary>
    [Fact]
    public void A_variant_keeps_the_sku_its_source_gave_it()
    {
        var product = InTwoColoursFromAStore().ToNewProduct("shopify", Clock, Colours.Defined());

        Assert.Equal(["MOKA-OLV", "MOKA-BLK"], product.Variants.Select(variant => variant.Sku));
    }

    /// <summary>
    /// Without one, or with one two variants share, the SKU is ours as before:
    /// the product's id and the source's key. A shared SKU would make two
    /// variants one in every other context.
    /// </summary>
    [Fact]
    public void A_variant_with_no_sku_or_a_shared_one_gets_ours()
    {
        var withoutOne = InTwoColoursFromAStore(oliveSku: null).ToNewProduct("shopify", Clock, Colours.Defined());
        var shared = InTwoColoursFromAStore(oliveSku: "MOKA", blackSku: "MOKA").ToNewProduct("shopify", Clock, Colours.Defined());

        Assert.Equal(["B073WXYZ01-44001", "MOKA-BLK"], withoutOne.Variants.Select(variant => variant.Sku));
        Assert.Equal(["B073WXYZ01-44001", "B073WXYZ01-44002"], shared.Variants.Select(variant => variant.Sku));
    }

    /// <summary>
    /// A variant the source prices costs that; one it does not costs the
    /// product's price, which is all the seed ever says (spec decision 2).
    /// </summary>
    [Fact]
    public void A_variant_costs_its_own_price_when_the_source_gives_one()
    {
        var product = InTwoColoursFromAStore().ToNewProduct("shopify", Clock, Colours.Defined());

        Assert.Equal(
            [("MOKA-OLV", 29.90m), ("MOKA-BLK", 32.50m)],
            product.Variants.Select(variant => (variant.Sku, variant.Price.Amount)));
    }

    /// <summary>
    /// The barcode is the source's, and follows it on every import.
    /// </summary>
    [Fact]
    public void The_barcode_is_kept_and_follows_the_source()
    {
        var product = InTwoColoursFromAStore().ToNewProduct("shopify", Clock, Colours.Defined());

        var relabelled = InTwoColoursFromAStore();
        relabelled = relabelled with
        {
            Variants = [relabelled.Variants![0] with { Barcode = "8400000000035" }, relabelled.Variants[1]],
        };
        relabelled.ApplyTo(product, Clock, Colours.Defined(), "shopify");

        Assert.Equal(["8400000000035", "8400000000028"], product.Variants.Select(variant => variant.Barcode));
    }

    /// <summary>
    /// A variant is found again by the source's id for it, so a SKU renamed in
    /// the store adds no second variant. And ours is never renamed: Inventory,
    /// the cart and every order speak it (spec, *Re-importing*, corrected).
    /// </summary>
    [Fact]
    public void A_variant_is_found_by_its_source_id_and_keeps_its_sku_when_the_source_renames_it()
    {
        var product = InTwoColoursFromAStore().ToNewProduct("shopify", Clock, Colours.Defined());

        InTwoColoursFromAStore(oliveSku: "MOKA-OLIVE-V2", blackPrice: 30m).ApplyTo(product, Clock, Colours.Defined(), "shopify");

        Assert.Equal(
            [("MOKA-OLV", 29.90m), ("MOKA-BLK", 30m)],
            product.Variants.Select(variant => (variant.Sku, variant.Price.Amount)));
        Assert.Equal(
            [new ExternalReference("shopify", "44001")],
            product.Variants[0].ExternalReferences);
    }

    /// <summary>
    /// A variant is taxed in the class its source says (phase 16, S2a, P16-9): a
    /// store that marks something not taxable makes it <c>exempt</c>, and a
    /// re-import follows the source. A source that says nothing leaves the class
    /// as it is — the seed's variants keep theirs, which is none, read as
    /// standard (ADR 0028).
    /// </summary>
    [Fact]
    public void A_variant_is_taxed_in_the_class_its_source_says_and_keeps_its_own_when_the_source_is_silent()
    {
        var store = InTwoColoursFromAStore();
        store = store with { Variants = [store.Variants![0] with { TaxClass = "exempt" }, store.Variants[1] with { TaxClass = "standard" }] };
        var product = store.ToNewProduct("shopify", Clock, Colours.Defined());
        Assert.Equal(["exempt", "standard"], product.Variants.Select(variant => variant.TaxClass));

        var changed = store with { Variants = [store.Variants[0] with { TaxClass = "standard" }, store.Variants[1] with { TaxClass = null }] };
        changed.ApplyTo(product, Clock, Colours.Defined(), "shopify");

        Assert.Equal(["standard", "standard"], product.Variants.Select(variant => variant.TaxClass));
        Assert.Null(Assert.Single(AnExternalProduct().ToNewProduct("seed", Clock).Variants).TaxClass);
    }

    /// <summary>
    /// The store's handle — the product's name in its URLs — is kept beside the
    /// id, so leaving the store can redirect its old addresses with no second
    /// import (spec decision 4). It follows the source; the link stays one.
    /// </summary>
    [Fact]
    public void The_sources_handle_is_kept_beside_its_id_and_follows_it()
    {
        var product = InTwoColoursFromAStore().ToNewProduct("shopify", Clock, Colours.Defined());

        (InTwoColoursFromAStore() with { Handle = "moka-coffee-maker-olive" }).ApplyTo(product, Clock, Colours.Defined(), "shopify");

        var reference = Assert.Single(product.ExternalReferences);
        Assert.Equal(("shopify", "B073WXYZ01", "moka-coffee-maker-olive"), (reference.Source, reference.ExternalId, reference.Handle));
    }
}
