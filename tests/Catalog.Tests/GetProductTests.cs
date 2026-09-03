using ElGuerre.Tendero.Catalog.Domain;
using ElGuerre.Tendero.Catalog.Features.GetProduct;
using ElGuerre.Tendero.Catalog.Ports;
using ElGuerre.Tendero.Inventory.Ports;
using ElGuerre.Tendero.SharedKernel;
using ElGuerre.Tendero.Tests;
using Xunit;

namespace ElGuerre.Tendero.Catalog.Tests.Features;

/// <summary>
/// The product page's query.
///
/// What is asserted here is the shape a page depends on: the slug resolves in
/// both directions, a picker gets every option and not only the sold ones, an
/// unpublished product does not exist, and stock is disclosed the way a shop
/// discloses it. The SQL belongs to the adapter and is covered against a real
/// Postgres in the integration tests — this suite would pass over a broken query
/// and says so out loud rather than pretending otherwise.
/// </summary>
public sealed class GetProductTests
{
    private static readonly TestClock Clock = new();

    [Fact]
    public async Task A_slug_resolves_to_the_product_it_names()
    {
        var result = await HandlerOver(AShirt()).HandleAsync(
            new GetProductQuery("camisa-de-lino", "es"), TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Equal("Camisa de lino", result.Name);
        Assert.Equal("camisa-de-lino", result.Slug);
    }

    /// <summary>
    /// The English page asked for by its Spanish URL still answers, and answers
    /// in English with its own canonical slug. That is the redirect the frontend
    /// needs, and returning 404 would throw away a visitor who is one hop from
    /// the page they wanted.
    /// </summary>
    [Fact]
    public async Task A_slug_from_another_culture_still_finds_the_product()
    {
        var result = await HandlerOver(AShirt()).HandleAsync(
            new GetProductQuery("camisa-de-lino", "en"), TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Equal("Linen shirt", result.Name);
        Assert.Equal("linen-shirt", result.Slug);
    }

    /// <summary>
    /// Every culture, including the one being answered in. hreflang requires the
    /// page to name its own canonical URL beside the alternates, and leaving
    /// itself out is the mistake hand-written implementations make.
    /// </summary>
    [Fact]
    public async Task The_page_carries_every_cultures_url()
    {
        var result = await HandlerOver(AShirt()).HandleAsync(
            new GetProductQuery("camisa-de-lino", "es"), TestContext.Current.CancellationToken);

        Assert.Equal(
            [("en", "linen-shirt"), ("es", "camisa-de-lino")],
            result!.Alternates.Select(alternate => (alternate.Culture, alternate.Slug)));
    }

    /// <summary>
    /// A Draft product is a 404 and not a 403. It is under review, it is not in
    /// the catalogue, and answering "exists but not for you" leaks the queue.
    /// </summary>
    [Fact]
    public async Task A_product_that_is_not_published_does_not_exist()
    {
        var draft = AShirt(publish: false);

        var result = await HandlerOver(draft).HandleAsync(
            new GetProductQuery("camisa-de-lino", "es"), TestContext.Current.CancellationToken);

        Assert.Null(result);
    }

    [Fact]
    public async Task An_unknown_slug_is_nothing()
    {
        var result = await HandlerOver(AShirt()).HandleAsync(
            new GetProductQuery("no-existe", "es"), TestContext.Current.CancellationToken);

        Assert.Null(result);
    }

    /// <summary>
    /// The picker gets the whole axis, not the options that happen to have a
    /// variant. This is the false-positive facet of ADR 0015 turned around: a
    /// size that exists in navy and not in black has to render disabled, and it
    /// cannot render at all if the response never mentions it.
    /// </summary>
    [Fact]
    public async Task An_axis_offers_every_option_the_catalogue_defines()
    {
        // The shirt sells navy in 38 and 40, and black only in 38.
        var result = await HandlerOver(AShirt()).HandleAsync(
            new GetProductQuery("camisa-de-lino", "es"), TestContext.Current.CancellationToken);

        var colour = Assert.Single(result!.Axes, axis => axis.Code == "COLOR");
        Assert.Equal(["NAVY_BLUE", "BLACK"], colour.Options.Select(option => option.Code));
        Assert.Equal(["Azul marino", "Negro"], colour.Options.Select(option => option.Label));

        Assert.Equal(3, result.Variants.Count);
        Assert.DoesNotContain(result.Variants, variant => variant.Sku == "SHIRT-BLACK-40");
    }

    /// <summary>
    /// The label a page shows is translated; the one an order freezes is not.
    /// <c>Variant.LabelFor</c> joins the option CODES on purpose, because a
    /// frozen label must not depend on a translation somebody can edit later.
    /// </summary>
    [Fact]
    public async Task A_variant_reads_in_the_shoppers_language()
    {
        var spanish = await HandlerOver(AShirt()).HandleAsync(
            new GetProductQuery("camisa-de-lino", "es"), TestContext.Current.CancellationToken);

        var english = await HandlerOver(AShirt()).HandleAsync(
            new GetProductQuery("linen-shirt", "en"), TestContext.Current.CancellationToken);

        Assert.Equal("Azul marino · 38", Navy38(spanish!).Label);
        Assert.Equal("Navy blue · 38", Navy38(english!).Label);
    }

    /// <summary>The axes' declared order is data (ADR 0015): colour then size,
    /// never the other way round.</summary>
    [Fact]
    public async Task The_axes_keep_the_order_the_catalogue_declared()
    {
        var result = await HandlerOver(AShirt()).HandleAsync(
            new GetProductQuery("camisa-de-lino", "es"), TestContext.Current.CancellationToken);

        Assert.Equal(["COLOR", "SIZE"], result!.Axes.Select(axis => axis.Code));
    }

    /// <summary>
    /// Stock is a boolean until it is nearly gone. The exact number is only shown
    /// when it is a reason to hurry — and a shop that publishes its shelf depth
    /// publishes it to its competitors too.
    /// </summary>
    [Fact]
    public async Task The_shelf_depth_is_only_disclosed_when_it_is_running_out()
    {
        var stock = new StockOf(("SHIRT-NAVY-38", 40), ("SHIRT-NAVY-40", 2), ("SHIRT-BLACK-38", 0));

        var result = await HandlerOver(AShirt(), stock).HandleAsync(
            new GetProductQuery("camisa-de-lino", "es"), TestContext.Current.CancellationToken);

        Assert.NotNull(result);

        var plenty = Variant(result, "SHIRT-NAVY-38");
        Assert.True(plenty.InStock);
        Assert.Null(plenty.Remaining);

        var nearlyGone = Variant(result, "SHIRT-NAVY-40");
        Assert.True(nearlyGone.InStock);
        Assert.Equal(2, nearlyGone.Remaining);

        var soldOut = Variant(result, "SHIRT-BLACK-38");
        Assert.False(soldOut.InStock);
        Assert.Null(soldOut.Remaining);
    }

    /// <summary>
    /// A SKU inventory has never heard of is out of stock, not an exception. A
    /// product page that threw because a warehouse row was missing would take the
    /// shop down over bookkeeping.
    /// </summary>
    [Fact]
    public async Task A_sku_with_no_stock_row_is_simply_sold_out()
    {
        var result = await HandlerOver(AShirt(), new StockOf()).HandleAsync(
            new GetProductQuery("camisa-de-lino", "es"), TestContext.Current.CancellationToken);

        Assert.All(result!.Variants, variant => Assert.False(variant.InStock));
    }

    /// <summary>The whole branch, not the leaf: "Hogar > Ropa" is where the
    /// shopper is and the leaf alone does not say it.</summary>
    [Fact]
    public async Task The_breadcrumb_is_the_whole_branch()
    {
        var result = await HandlerOver(AShirt()).HandleAsync(
            new GetProductQuery("camisa-de-lino", "es"), TestContext.Current.CancellationToken);

        Assert.Equal(["HOME", "CLOTHING"], result!.Category.Select(step => step.Code));
        Assert.Equal(["Hogar", "Ropa"], result.Category.Select(step => step.Name));
    }

    /// <summary>
    /// A number keeps its unit apart and a boolean keeps its value apart, because
    /// "40 °C" and "Sí" are interface copy: the API answers 40 and true, and the
    /// frontend's translation files decide how they read.
    /// </summary>
    [Fact]
    public async Task An_attribute_is_returned_typed_and_never_phrased()
    {
        var result = await HandlerOver(AShirt()).HandleAsync(
            new GetProductQuery("camisa-de-lino", "es"), TestContext.Current.CancellationToken);

        var washing = Assert.Single(result!.Attributes, a => a.Code == "WASH_TEMPERATURE");
        Assert.Equal(30m, washing.Number);
        Assert.Equal("°C", washing.Unit);
        Assert.Null(washing.Value);

        var iron = Assert.Single(result.Attributes, a => a.Code == "IRONABLE");
        Assert.True(iron.Flag);
        Assert.Null(iron.Value);

        var material = Assert.Single(result.Attributes, a => a.Code == "MATERIAL");
        Assert.Equal("Lino", material.Value);
    }

    /// <summary>
    /// A variant with no photo of its own borrows the product's cover: a size does
    /// not change what a thing looks like, a colour does, and only the ones that
    /// differ carry an image.
    /// </summary>
    [Fact]
    public async Task A_variant_without_a_photo_borrows_the_products()
    {
        var result = await HandlerOver(AShirt()).HandleAsync(
            new GetProductQuery("camisa-de-lino", "es"), TestContext.Current.CancellationToken);

        Assert.All(result!.Variants, variant => Assert.Equal("cover", variant.ImageId));
    }

    /// <summary>
    /// The range comes from the AVAILABLE variants, which is what a card promises
    /// and what a page has to agree with.
    /// </summary>
    [Fact]
    public async Task The_price_range_spans_what_can_be_bought()
    {
        var result = await HandlerOver(AShirt()).HandleAsync(
            new GetProductQuery("camisa-de-lino", "es"), TestContext.Current.CancellationToken);

        Assert.Equal(29.90m, result!.PriceFrom);
        Assert.Equal(34.90m, result.PriceTo);
        Assert.Equal("EUR", result.PriceCurrency);
    }

    // ---------- fixtures ----------

    private static VariantView Navy38(ProductDetail detail) => Variant(detail, "SHIRT-NAVY-38");

    private static VariantView Variant(ProductDetail detail, string sku) =>
        Assert.Single(detail.Variants, variant => variant.Sku == sku);

    private static GetProductHandler HandlerOver(Product product, IAvailabilityReader? stock = null) =>
        new(new InMemoryProductCatalogReader(product),
            new Definitions(),
            new Categories(),
            stock ?? new StockOf(
                ("SHIRT-NAVY-38", 10), ("SHIRT-NAVY-40", 10), ("SHIRT-BLACK-38", 10)));

    private static Product AShirt(bool publish = true)
    {
        var product = Product.Create(
            Clock,
            new LocalizedText(new Dictionary<string, string>
            {
                ["es"] = "Camisa de lino",
                ["en"] = "Linen shirt"
            }),
            new Money(29.90m, "EUR"),
            category: "CLOTHING");

        product.AddImage(Clock, new ImageId("cover"));

        product.SetAttribute(Clock, AttributeValue.Localized("MATERIAL",
            new LocalizedText(new Dictionary<string, string> { ["es"] = "Lino", ["en"] = "Linen" })));
        product.SetAttribute(Clock, AttributeValue.Numeric("WASH_TEMPERATURE", 30m));
        product.SetAttribute(Clock, AttributeValue.Boolean("IRONABLE", true));

        product.DefineAxes(Clock, ["COLOR", "SIZE"]);
        product.AddVariant(Clock, "SHIRT-NAVY-38", new Money(29.90m, "EUR"),
            new Dictionary<string, string> { ["COLOR"] = "NAVY_BLUE", ["SIZE"] = "38" });
        product.AddVariant(Clock, "SHIRT-NAVY-40", new Money(34.90m, "EUR"),
            new Dictionary<string, string> { ["COLOR"] = "NAVY_BLUE", ["SIZE"] = "40" });
        product.AddVariant(Clock, "SHIRT-BLACK-38", new Money(31.90m, "EUR"),
            new Dictionary<string, string> { ["COLOR"] = "BLACK", ["SIZE"] = "38" });

        if (publish)
            product.Publish(Clock);

        return product;
    }

    private sealed class Definitions : IAttributeDefinitionReader
    {
        public Task<AttributeDefinitions> AllAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new AttributeDefinitions(
            [
                Option("COLOR", "Color", "Colour",
                    ("NAVY_BLUE", "Azul marino", "Navy blue"),
                    ("BLACK", "Negro", "Black")),
                Option("SIZE", "Talla", "Size", ("38", "38", "38"), ("40", "40", "40")),
                Define("MATERIAL", "Material", "Material", AttributeKind.LocalizedText),
                Define("WASH_TEMPERATURE", "Temperatura de lavado", "Wash temperature",
                    AttributeKind.Number, unit: "°C"),
                Define("IRONABLE", "Planchable", "Ironable", AttributeKind.Boolean)
            ]));

        private static AttributeDefinition Define(
            string code, string es, string en, AttributeKind kind, string? unit = null) =>
            AttributeDefinition.Define(
                Clock, code,
                new LocalizedText(new Dictionary<string, string> { ["es"] = es, ["en"] = en }),
                kind, unit: unit, isVariantAxis: kind == AttributeKind.Option);

        private static AttributeDefinition Option(
            string code, string es, string en, params (string Code, string Es, string En)[] options)
        {
            var definition = Define(code, es, en, AttributeKind.Option);

            foreach (var (optionCode, optionEs, optionEn) in options)
            {
                definition.AddOption(Clock, optionCode,
                    new LocalizedText(new Dictionary<string, string>
                    {
                        ["es"] = optionEs,
                        ["en"] = optionEn
                    }));
            }

            return definition;
        }
    }

    private sealed class Categories : ICategoryReader
    {
        public Task<CategoryTree> AllAsync(CancellationToken cancellationToken = default)
        {
            var home = Category.Define(Clock, "HOME",
                new LocalizedText(new Dictionary<string, string> { ["es"] = "Hogar", ["en"] = "Home" }));

            var clothing = Category.Define(Clock, "CLOTHING",
                new LocalizedText(new Dictionary<string, string> { ["es"] = "Ropa", ["en"] = "Clothing" }),
                parent: home);

            return Task.FromResult(new CategoryTree([home, clothing]));
        }
    }

    private sealed class StockOf(params (string Sku, int Available)[] shelves) : IAvailabilityReader
    {
        public Task<IReadOnlyDictionary<string, int>> AvailableAsync(
            IReadOnlyCollection<string> skus, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<string, int>>(
                shelves.Where(shelf => skus.Contains(shelf.Sku))
                       .ToDictionary(shelf => shelf.Sku, shelf => shelf.Available));
    }
}
