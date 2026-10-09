using ElGuerre.Tendero.Catalog.Domain;
using ElGuerre.Tendero.Catalog.Features.CatalogueGaps;
using ElGuerre.Tendero.Catalog.Ports;
using ElGuerre.Tendero.SharedKernel;
using ElGuerre.Tendero.Tests;
using Xunit;

namespace ElGuerre.Tendero.Catalog.Tests.Features;

/// <summary>
/// What the catalogue lacks, one rule per kind (S-3a): Steward's
/// <c>catalogue_gaps</c> and a screen's question alike. Fifty rows at most, always
/// with the total.
/// </summary>
public sealed class CatalogueGapsTests
{
    private static readonly TestClock Clock = new();

    [Fact]
    public void A_name_with_no_english_is_a_gap_in_english_and_not_in_spanish()
    {
        var product = Spanish("Flexo articulado");

        Assert.True(CatalogueGapRules.Has(product, CatalogueGap.MissingName, "en"));
        Assert.False(CatalogueGapRules.Has(product, CatalogueGap.MissingName, "es"));
    }

    [Fact]
    public void A_product_with_no_picture_is_missing_an_image()
    {
        var product = Spanish("Flexo");
        Assert.True(CatalogueGapRules.Has(product, CatalogueGap.MissingImage, null));

        product.AddImage(Clock, new ImageId("flexo-1"));
        Assert.False(CatalogueGapRules.Has(product, CatalogueGap.MissingImage, null));
    }

    /// <summary>An alt text with no English is a gap; a product with no picture at
    /// all is the other gap, not this one.</summary>
    [Fact]
    public void A_picture_described_only_in_spanish_is_missing_its_english_alt()
    {
        var bare = Spanish("Flexo");
        var described = Spanish("Mochila");
        described.AddImage(Clock, new ImageId("mochila-1"), LocalizedText.From("es", "Mochila gris"));

        Assert.False(CatalogueGapRules.Has(bare, CatalogueGap.MissingImageAlt, "en"));
        Assert.True(CatalogueGapRules.Has(described, CatalogueGap.MissingImageAlt, "en"));
        Assert.False(CatalogueGapRules.Has(described, CatalogueGap.MissingImageAlt, "es"));
    }

    /// <summary>A picture with no alternative text at all lacks it in every language.</summary>
    [Fact]
    public void A_picture_with_no_alt_at_all_is_missing_it()
    {
        var product = Spanish("Flexo");
        product.AddImage(Clock, new ImageId("flexo-1"));

        Assert.True(CatalogueGapRules.Has(product, CatalogueGap.MissingImageAlt, "es"));
    }

    [Fact]
    public void A_draft_is_waiting_and_an_active_product_is_not()
    {
        var draft = Spanish("Flexo");
        var active = Spanish("Mochila");
        active.Publish(Clock);

        Assert.True(CatalogueGapRules.Has(draft, CatalogueGap.DraftWaiting, null));
        Assert.False(CatalogueGapRules.Has(active, CatalogueGap.DraftWaiting, null));
    }

    [Fact]
    public async Task Sixty_gaps_are_fifty_rows_and_a_total_of_sixty_in_code_order()
    {
        var products = Enumerable.Range(0, 60).Select(index => Spanish($"Producto {index}")).ToArray();

        var gaps = await Handler(products)
            .HandleAsync(new CatalogueGapsQuery(CatalogueGap.MissingName, "en"), TestContext.Current.CancellationToken);

        Assert.Equal((60, ReadLimits.MaxRows), (gaps.Total, gaps.Rows.Count));
        Assert.Equal(gaps.Rows.Select(row => row.Code).Order(StringComparer.Ordinal), gaps.Rows.Select(row => row.Code));
        Assert.StartsWith("Producto", gaps.Rows[0].Name);
    }

    /// <summary>The gap's culture and the reader's are two things: a product with no
    /// picture is named in the language the shopkeeper reads (final review, S-3a).</summary>
    [Fact]
    public async Task Names_are_in_the_readers_culture_and_the_gaps_culture_is_normalised()
    {
        var bilingual = Product.Create(Clock,
            new LocalizedText(new Dictionary<string, string> { ["es"] = "Mochila", ["en"] = "Backpack" }), new Money(10m, "EUR"));
        var handler = Handler(bilingual, Spanish("Flexo"));

        var pictures = await handler.HandleAsync(
            new CatalogueGapsQuery(CatalogueGap.MissingImage, null, DisplayCulture: "en"), TestContext.Current.CancellationToken);
        var english = await handler.HandleAsync(
            new CatalogueGapsQuery(CatalogueGap.MissingName, "EN"), TestContext.Current.CancellationToken);

        Assert.Contains("Backpack", pictures.Rows.Select(row => row.Name));
        Assert.Equal("en", english.Culture);
    }

    [Fact]
    public async Task An_archived_product_is_no_gap()
    {
        var archived = Spanish("Flexo");
        archived.Archive(Clock);

        var gaps = await Handler(archived)
            .HandleAsync(new CatalogueGapsQuery(CatalogueGap.MissingName, "en"), TestContext.Current.CancellationToken);

        Assert.Equal(0, gaps.Total);
    }

    [Theory]
    [InlineData(CatalogueGap.MissingName, null, 50, false)]
    [InlineData(CatalogueGap.MissingName, "fr", 50, false)]
    [InlineData(CatalogueGap.MissingName, "en", 50, true)]
    [InlineData(CatalogueGap.MissingImageAlt, null, 50, false)]
    [InlineData(CatalogueGap.MissingImage, null, 50, true)]
    [InlineData(CatalogueGap.DraftWaiting, null, 50, true)]
    [InlineData(CatalogueGap.DraftWaiting, "en", 50, false)]
    [InlineData(CatalogueGap.DraftWaiting, null, 51, false)]
    [InlineData(CatalogueGap.DraftWaiting, null, 0, false)]
    [InlineData(CatalogueGap.MissingRequiredAttribute, null, 50, true)]
    [InlineData(CatalogueGap.MissingRequiredAttribute, "en", 50, false)]
    public void A_gap_by_culture_needs_one_the_shop_speaks_and_the_others_take_none(
        CatalogueGap kind, string? culture, int limit, bool valid) =>
        Assert.Equal(valid, new CatalogueGapsValidator().Validate(new CatalogueGapsQuery(kind, culture, limit)).IsValid);

    // ---------- missing a required attribute (ADR 0056, amended) ----------

    /// <summary>Cookware must say its material; bags must say nothing.</summary>
    private static CategoryTree Requiring()
    {
        var home = Category.Define(Clock, "HOME", LocalizedText.Both("Home", "Hogar"), attributes: ["COLOR"], required: ["MATERIAL"]);
        var cookware = Category.Define(Clock, "COOKWARE", LocalizedText.Both("Cookware", "Menaje"), home, required: ["PIECES"]);
        var bags = Category.Define(Clock, "BAGS", LocalizedText.Both("Bags", "Bolsas"), attributes: ["MATERIAL"]);
        return new CategoryTree([home, cookware, bags]);
    }

    private static Product In(string category, string name, params string[] holds)
    {
        var product = Product.Create(Clock, LocalizedText.From("es", name), new Money(10m, "EUR"), category: category);
        foreach (var code in holds)
            product.SetAttribute(Clock, AttributeValue.Plain(code, "x"));
        return product;
    }

    [Fact]
    public void A_product_without_what_its_category_requires_is_incomplete_and_one_with_it_is_not()
    {
        var tree = Requiring();

        Assert.True(CatalogueGapRules.Has(In("COOKWARE", "Sartén", "MATERIAL"), CatalogueGap.MissingRequiredAttribute, null, tree));
        Assert.False(CatalogueGapRules.Has(In("COOKWARE", "Sartén", "MATERIAL", "PIECES"), CatalogueGap.MissingRequiredAttribute, null, tree));
        // Expected is not required: a bag with no material is not incomplete.
        Assert.False(CatalogueGapRules.Has(In("BAGS", "Mochila"), CatalogueGap.MissingRequiredAttribute, null, tree));
        // With nobody saying what is required, nothing is.
        Assert.False(CatalogueGapRules.Has(In("COOKWARE", "Sartén"), CatalogueGap.MissingRequiredAttribute, null));
    }

    /// <summary>
    /// The row says what it lacks, by the attribute's label in the reader's
    /// language — and by its code when the catalogue never defined it: a gap is
    /// not hidden behind another. The other kinds say nothing there; the kind is
    /// what is missing.
    /// </summary>
    [Fact]
    public async Task An_incomplete_product_says_what_it_lacks_in_the_readers_language()
    {
        var pan = In("COOKWARE", "Sartén");
        var complete = In("COOKWARE", "Olla", "MATERIAL", "PIECES");
        var material = AttributeDefinition.Define(Clock, "MATERIAL", LocalizedText.Both("material", "material"), AttributeKind.Text);
        var pieces = AttributeDefinition.Define(Clock, "PIECES", LocalizedText.Both("pieces", "piezas"), AttributeKind.Number);
        var handler = new CatalogueGapsHandler(
            new InMemoryProductCatalogReader(pan, complete), new Categories(Requiring()), new Definitions(material, pieces));

        var spanish = await handler.HandleAsync(new CatalogueGapsQuery(CatalogueGap.MissingRequiredAttribute, null), TestContext.Current.CancellationToken);
        var english = await handler.HandleAsync(
            new CatalogueGapsQuery(CatalogueGap.MissingRequiredAttribute, null, DisplayCulture: "en"), TestContext.Current.CancellationToken);
        var undefined = await new CatalogueGapsHandler(new InMemoryProductCatalogReader(pan), new Categories(Requiring()), new Definitions())
            .HandleAsync(new CatalogueGapsQuery(CatalogueGap.MissingRequiredAttribute, null), TestContext.Current.CancellationToken);
        var drafts = await handler.HandleAsync(new CatalogueGapsQuery(CatalogueGap.DraftWaiting, null), TestContext.Current.CancellationToken);

        Assert.Equal((1, pan.Code), (spanish.Total, Assert.Single(spanish.Rows).Code));
        // The root's first, then the leaf's.
        Assert.Equal(["material", "piezas"], spanish.Rows[0].Lacks);
        Assert.Equal(["material", "pieces"], english.Rows[0].Lacks);
        Assert.Equal(["MATERIAL", "PIECES"], undefined.Rows[0].Lacks);
        Assert.All(drafts.Rows, row => Assert.Empty(row.Lacks));
    }

    private static CatalogueGapsHandler Handler(params Product[] products) =>
        new(new InMemoryProductCatalogReader(products), new Categories(CategoryTree.Empty), new Definitions());

    private sealed class Categories(CategoryTree tree) : ICategoryReader
    {
        public Task<CategoryTree> AllAsync(CancellationToken cancellationToken = default) => Task.FromResult(tree);
    }

    private sealed class Definitions(params AttributeDefinition[] definitions) : IAttributeDefinitionReader
    {
        public Task<AttributeDefinitions> AllAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new AttributeDefinitions(definitions));
    }

    private static Product Spanish(string name) =>
        Product.Create(Clock, LocalizedText.From("es", name), new Money(10m, "EUR"));
}
