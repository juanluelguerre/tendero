using ElGuerre.Tendero.Catalog.Domain;
using ElGuerre.Tendero.Catalog.Ports;
using ElGuerre.Tendero.Search.Contracts;
using ElGuerre.Tendero.SharedKernel;
using ElGuerre.Tendero.Tests;
using Xunit;

namespace ElGuerre.Tendero.Search.Tests;

/// <summary>
/// What goes into the index, per variant and per culture (ADR 0015).
///
/// `docs/testing.md` has called this document "a public contract with
/// Elasticsearch" since the first week, and it had no test of its own. The
/// SearchEval gate measures what it does to ranking over the whole seed; it
/// cannot say WHICH field went wrong, and several of these fields are not
/// scored at all — stock, price, the category codes a filter reads — so a gate
/// that stays green says nothing about them.
/// </summary>
public sealed class ProductSearchDocumentTests
{
    private static readonly TestClock Clock = new();

    // ---------- One document per variant that can be bought ----------

    [Fact]
    public void Every_available_variant_is_one_document_and_nothing_else_is()
    {
        var shirt = AShirt();

        var documents = Documents(shirt, "es");

        Assert.Equal(["SHIRT-BLACK-38", "SHIRT-NAVY-38", "SHIRT-NAVY-40"], documents.Select(doc => doc.Sku).Order());
        Assert.All(documents, doc => Assert.Equal(shirt.Id.ToString(), doc.ProductId));
        Assert.Equal(
            shirt.Variants.Where(variant => variant.Status == VariantStatus.Available).Select(variant => variant.Id.ToString()).Order(),
            documents.Select(doc => doc.Id).Order());
    }

    [Fact]
    public void A_discontinued_variant_is_not_indexed()
    {
        var shirt = AShirt();
        shirt.DiscontinueVariant(Clock, "SHIRT-BLACK-38");

        Assert.DoesNotContain(Documents(shirt, "es"), doc => doc.Sku == "SHIRT-BLACK-38");
    }

    // ---------- Price: exact per variant, the range per product ----------

    [Fact]
    public void Each_document_carries_its_own_price_and_the_whole_products_range()
    {
        var navy40 = Documents(AShirt(), "es").Single(doc => doc.Sku == "SHIRT-NAVY-40");

        Assert.Equal(34.90m, navy40.PriceAmount);
        Assert.Equal(29.90m, navy40.PriceFrom);
        Assert.Equal(34.90m, navy40.PriceTo);
        Assert.Equal("EUR", navy40.PriceCurrency);
    }

    /// <summary>A card must not advertise "up to 34,90 €" for a price nobody can
    /// buy any more.</summary>
    [Fact]
    public void The_range_counts_only_what_can_still_be_bought()
    {
        var shirt = AShirt();
        shirt.DiscontinueVariant(Clock, "SHIRT-NAVY-40");

        Assert.All(Documents(shirt, "es"), doc => Assert.Equal(31.90m, doc.PriceTo));
    }

    // ---------- Stock ----------

    [Fact]
    public void A_variant_is_in_stock_only_when_units_are_available()
    {
        var documents = Documents(AShirt(), "es",
            available: new Dictionary<string, int> { ["SHIRT-NAVY-38"] = 3, ["SHIRT-NAVY-40"] = 0 });

        Assert.True(documents.Single(doc => doc.Sku == "SHIRT-NAVY-38").InStock);
        Assert.False(documents.Single(doc => doc.Sku == "SHIRT-NAVY-40").InStock);
    }

    /// <summary>A SKU nobody has counted is a SKU nobody can ship. Defaulting the
    /// other way is how a shop sells what it does not have.</summary>
    [Fact]
    public void A_variant_with_no_stock_row_is_not_in_stock()
    {
        var documents = Documents(AShirt(), "es",
            available: new Dictionary<string, int> { ["SHIRT-NAVY-38"] = 3 });

        Assert.False(documents.Single(doc => doc.Sku == "SHIRT-BLACK-38").InStock);
    }

    // ---------- Text, in the index's culture ----------

    [Fact]
    public void Names_slugs_and_descriptions_are_in_the_documents_culture()
    {
        var shirt = AShirt();

        var es = Documents(shirt, "es").First();
        var en = Documents(shirt, "en").First();

        Assert.Equal(("es", "Camisa de lino", "Camisa ligera"), (es.Culture, es.Name, es.Description));
        Assert.Equal(("en", "Linen shirt", "Light shirt"), (en.Culture, en.Name, en.Description));
        Assert.Equal(shirt.Slug.In("es"), es.Slug);
        Assert.Equal(shirt.Slug.In("en"), en.Slug);
        Assert.NotEqual(es.Slug, en.Slug);
    }

    /// <summary>
    /// The reason this rendering exists. It used to write the raw data, so the
    /// English index said "azul marino" and "navy blue shoes" scored 0.000.
    /// Each culture gets the definition's label AND the option's label.
    /// </summary>
    [Fact]
    public void Attributes_are_rendered_with_the_labels_of_the_documents_culture()
    {
        var shirt = AShirt();
        shirt.SetAttribute(Clock, AttributeValue.Option("COLOR", "NAVY_BLUE"));

        var es = Documents(shirt, "es").First().AttributesText;
        var en = Documents(shirt, "en").First().AttributesText;

        Assert.Contains("Color Azul marino", es);
        Assert.Contains("Colour Navy blue", en);
        Assert.DoesNotContain("Azul", en);
        Assert.Contains("Material Lino", es);
        Assert.Contains("Material Linen", en);
    }

    [Fact]
    public void A_number_is_rendered_with_its_unit()
    {
        Assert.Contains("Wash temperature 30 °C", Documents(AShirt(), "en").First().AttributesText);
    }

    /// <summary>What somebody types is the attribute's name — "ironable" — and
    /// only when it is true. A false flag in the index would match the word and
    /// promise the opposite.</summary>
    [Fact]
    public void A_true_flag_is_indexed_by_its_name_and_a_false_one_is_not_indexed()
    {
        var yes = AShirt();
        var no = AShirt();
        no.SetAttribute(Clock, AttributeValue.Boolean("IRONABLE", false));

        Assert.Contains("Ironable", Documents(yes, "en").First().AttributesText);
        Assert.DoesNotContain("Ironable", Documents(no, "en").First().AttributesText);
    }

    [Fact]
    public void An_attribute_marked_not_searchable_stays_out_of_the_text()
    {
        var shirt = AShirt();
        shirt.SetAttribute(Clock, AttributeValue.Plain("EAN", "8412345678905"));

        Assert.DoesNotContain("8412345678905", Documents(shirt, "en").First().AttributesText);
    }

    [Fact]
    public void An_attribute_with_no_definition_is_indexed_by_its_code()
    {
        var shirt = AShirt();
        shirt.SetAttribute(Clock, AttributeValue.Plain("FIT", "slim"));

        Assert.Contains("FIT slim", Documents(shirt, "en").First().AttributesText);
    }

    [Fact]
    public void A_product_with_nothing_worth_indexing_has_no_attribute_text()
    {
        var bare = Product.Create(Clock, Text("Taza", "Mug"), new Money(9.90m, "EUR"));
        bare.AddVariant(Clock, "MUG-DEFAULT", new Money(9.90m, "EUR"));

        Assert.Null(Documents(bare, "en").Single().AttributesText);
    }

    // ---------- The category, twice ----------

    [Fact]
    public void The_category_travels_as_a_code_as_its_branch_and_as_the_branch_in_words()
    {
        var es = Documents(AShirt(), "es").First();
        var en = Documents(AShirt(), "en").First();

        Assert.Equal("CLOTHING", es.Category);
        Assert.Equal(["HOME", "CLOTHING"], es.CategoryCodes);
        Assert.Equal("Hogar Ropa", es.CategoryPathText);
        Assert.Equal("Home Clothing", en.CategoryPathText);
    }

    // ---------- The rest of the card ----------

    [Fact]
    public void Axis_values_are_written_as_axis_and_option_pairs()
    {
        var navy38 = Documents(AShirt(), "es").Single(doc => doc.Sku == "SHIRT-NAVY-38");

        Assert.Equal(["COLOR:NAVY_BLUE", "SIZE:38"], navy38.AxisValues.Order());
    }

    /// <summary>Colour needs its own photo, size does not: the variant's image
    /// when it has one, the product's cover otherwise — and the cover is the
    /// lowest sort order, which is the aggregate's rule, not the list's order.</summary>
    [Fact]
    public void A_variant_shows_its_own_image_and_otherwise_the_products_cover()
    {
        var shirt = AShirt();
        shirt.SetVariantImage(Clock, "SHIRT-BLACK-38", new ImageId("black"));

        var documents = Documents(shirt, "es");

        Assert.Equal("black", documents.Single(doc => doc.Sku == "SHIRT-BLACK-38").ImageId);
        Assert.Equal("cover", documents.Single(doc => doc.Sku == "SHIRT-NAVY-38").ImageId);
    }

    [Fact]
    public void The_document_carries_the_products_public_code_and_its_status_in_lower_case()
    {
        var shirt = AShirt();

        var document = Documents(shirt, "es").First();

        Assert.Equal(shirt.Code, document.Code);
        Assert.Equal("active", document.Status);
    }

    [Theory]
    [InlineData("es", "products_es")]
    [InlineData("en", "products_en")]
    public void Each_culture_has_its_own_index(string culture, string index)
    {
        Assert.Equal(index, ProductSearchDocument.IndexNameFor(culture));
    }

    // ---------- The projection's rule ----------

    [Fact]
    public async Task Only_an_active_product_is_indexed_and_anything_else_is_removed()
    {
        var active = AShirt();
        var draft = AShirt(publish: false);
        var indexer = new RecordingIndexer();

        Assert.True(await ProductIndexProjection.ApplyAsync(indexer, active, CancellationToken.None));
        Assert.False(await ProductIndexProjection.ApplyAsync(indexer, draft, CancellationToken.None));

        Assert.Equal([active.Id], indexer.Indexed);
        Assert.Equal([draft.Id], indexer.Removed);
    }

    // ---------- Builders ----------

    private static IReadOnlyList<ProductSearchDocument> Documents(
        Product product, string culture, IReadOnlyDictionary<string, int>? available = null) =>
        [.. ProductSearchDocument.ForVariants(product, culture, Definitions, Categories, available)];

    private static Product AShirt(bool publish = true)
    {
        var product = Product.Create(
            Clock, Text("Camisa de lino", "Linen shirt"), new Money(29.90m, "EUR"),
            description: Text("Camisa ligera", "Light shirt"), category: "CLOTHING");

        product.AddImage(Clock, new ImageId("cover"));
        product.AddImage(Clock, new ImageId("detail"));

        product.SetAttribute(Clock, AttributeValue.Localized("MATERIAL", Text("Lino", "Linen")));
        product.SetAttribute(Clock, AttributeValue.Numeric("WASH_TEMPERATURE", 30m));
        product.SetAttribute(Clock, AttributeValue.Boolean("IRONABLE", true));

        // The implicit default every import mints, superseded by the matrix.
        product.AddVariant(Clock, "SHIRT-DEFAULT", new Money(29.90m, "EUR"));
        product.DefineAxes(Clock, ["COLOR", "SIZE"]);
        product.AddVariant(Clock, "SHIRT-NAVY-38", new Money(29.90m, "EUR"), Axes("NAVY_BLUE", "38"));
        product.AddVariant(Clock, "SHIRT-NAVY-40", new Money(34.90m, "EUR"), Axes("NAVY_BLUE", "40"));
        product.AddVariant(Clock, "SHIRT-BLACK-38", new Money(31.90m, "EUR"), Axes("BLACK", "38"));

        if (publish)
            product.Publish(Clock);

        return product;
    }

    private static Dictionary<string, string> Axes(string color, string size) =>
        new() { ["COLOR"] = color, ["SIZE"] = size };

    private static LocalizedText Text(string es, string en) =>
        new(new Dictionary<string, string> { ["es"] = es, ["en"] = en });

    private static readonly AttributeDefinitions Definitions = new(
    [
        Option("COLOR", "Color", "Colour", ("NAVY_BLUE", "Azul marino", "Navy blue"), ("BLACK", "Negro", "Black")),
        Option("SIZE", "Talla", "Size", ("38", "38", "38"), ("40", "40", "40")),
        Define("MATERIAL", "Material", "Material", AttributeKind.LocalizedText),
        Define("WASH_TEMPERATURE", "Temperatura de lavado", "Wash temperature", AttributeKind.Number, unit: "°C"),
        Define("IRONABLE", "Planchable", "Ironable", AttributeKind.Boolean),
        Define("EAN", "EAN", "EAN", AttributeKind.Text, searchable: false)
    ]);

    private static readonly CategoryTree Categories = BuildCategories();

    private static CategoryTree BuildCategories()
    {
        var home = Category.Define(Clock, "HOME", Text("Hogar", "Home"));
        var clothing = Category.Define(Clock, "CLOTHING", Text("Ropa", "Clothing"), parent: home);
        return new CategoryTree([home, clothing]);
    }

    private static AttributeDefinition Define(
        string code, string es, string en, AttributeKind kind, string? unit = null, bool searchable = true) =>
        AttributeDefinition.Define(
            Clock, code, Text(es, en), kind, unit: unit,
            isVariantAxis: kind == AttributeKind.Option, isSearchable: searchable);

    private static AttributeDefinition Option(
        string code, string es, string en, params (string Code, string Es, string En)[] options)
    {
        var definition = Define(code, es, en, AttributeKind.Option);

        foreach (var (optionCode, optionEs, optionEn) in options)
            definition.AddOption(Clock, optionCode, Text(optionEs, optionEn));

        return definition;
    }

    private sealed class RecordingIndexer : IProductIndexer
    {
        public List<ProductId> Indexed { get; } = [];
        public List<ProductId> Removed { get; } = [];

        public Task IndexAsync(Product product, CancellationToken ct = default)
        {
            Indexed.Add(product.Id);
            return Task.CompletedTask;
        }

        public Task RemoveAsync(ProductId productId, CancellationToken ct = default)
        {
            Removed.Add(productId);
            return Task.CompletedTask;
        }

        public Task RecreateAsync(CancellationToken ct = default) => Task.CompletedTask;
    }
}
