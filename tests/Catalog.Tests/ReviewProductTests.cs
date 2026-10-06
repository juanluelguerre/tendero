using ElGuerre.Tendero.Catalog.Domain;
using ElGuerre.Tendero.Catalog.Features.ReviewProduct;
using ElGuerre.Tendero.Catalog.Ports;
using ElGuerre.Tendero.SharedKernel;
using ElGuerre.Tendero.Tests;
using Xunit;

namespace ElGuerre.Tendero.Catalog.Tests.Features;

/// <summary>
/// A product as the shopkeeper reviews it (the redesign's R-4.3): what the
/// product page cannot show — a draft, and every culture as stored, with an
/// absence left absent rather than filled from another language.
/// </summary>
public sealed class ReviewProductTests
{
    private static readonly TestClock Clock = new();

    [Fact]
    public async Task Shows_a_draft_with_its_texts_as_stored_and_says_what_is_missing()
    {
        var draft = Product.Create(
            Clock,
            LocalizedText.From("es", "Camiseta técnica"),
            new Money(24.90m, "EUR"),
            description: LocalizedText.From("es", "Transpirable y ligera."),
            brand: "Vertex",
            category: "CLOTHING");

        var review = await Handler(draft).HandleAsync(new ReviewProductQuery(draft.Code, "en"), TestContext.Current.CancellationToken);

        Assert.NotNull(review);
        Assert.Equal(ProductStatus.Draft, review.Status);
        Assert.Equal(new Dictionary<string, string> { ["es"] = "Camiseta técnica" }, review.Names);
        // No English description falls back to the Spanish one: the gap is what the review is for.
        Assert.False(review.Descriptions.ContainsKey("en"));
        Assert.Equal(["en"], review.MissingCultures);
        Assert.Equal("Clothing", review.Category);
        Assert.Equal(24.90m, review.PriceAmount);
    }

    [Fact]
    public async Task Shows_every_image_in_order_and_an_undefined_attribute_by_its_code()
    {
        var product = Product.Create(Clock, LocalizedText.From("es", "Mochila"), new Money(49m, "EUR"));
        product.AddImage(Clock, new ImageId("second"));
        product.AddImage(Clock, new ImageId("third"));
        product.SetAttribute(Clock, new AttributeValue("LITRES", AttributeKind.Number, Number: 22));

        var review = await Handler(product).HandleAsync(new ReviewProductQuery(product.Code, "es"), TestContext.Current.CancellationToken);

        Assert.NotNull(review);
        Assert.Equal(["second", "third"], review.ImageIds);
        var litres = Assert.Single(review.Attributes);
        Assert.Equal("LITRES", litres.Label);
        Assert.Equal(22m, litres.Number);
    }

    /// <summary>
    /// What each picture says, per language and as stored (phase 13a): a language a
    /// picture is not described in is absent, never filled from the other — the
    /// screen where a shopkeeper types alt text shows exactly what is missing.
    /// </summary>
    [Fact]
    public async Task Shows_what_each_picture_says_in_each_language_as_stored()
    {
        var product = Product.Create(Clock, LocalizedText.From("es", "Taza"), new Money(9m, "EUR"));
        product.AddImage(Clock, new ImageId("cover"), LocalizedText.Both("A white mug.", "Una taza blanca."));
        product.AddImage(Clock, new ImageId("side"), LocalizedText.From("es", "La taza de lado."));
        product.AddImage(Clock, new ImageId("top"));

        var review = (await Handler(product).HandleAsync(new ReviewProductQuery(product.Code, "en"), TestContext.Current.CancellationToken))!;

        Assert.Equal(["cover", "side", "top"], review.Images.Select(image => image.Id));
        Assert.Equal(("Una taza blanca.", "A white mug."), (review.Images[0].Alt["es"], review.Images[0].Alt["en"]));
        Assert.Equal(["es"], review.Images[1].Alt.Keys);
        Assert.Empty(review.Images[2].Alt);
        // The ids alone are still there, in the same order, for whoever only draws them.
        Assert.Equal(review.Images.Select(image => image.Id), review.ImageIds);
    }

    [Fact]
    public async Task Names_a_defined_attribute_by_its_label_in_the_culture_asked_for()
    {
        var product = Product.Create(Clock, LocalizedText.From("es", "Mochila"), new Money(49m, "EUR"));
        product.SetAttribute(Clock, new AttributeValue("LITRES", AttributeKind.Number, Number: 22));
        var litres = AttributeDefinition.Define(Clock, "LITRES",
            new LocalizedText(new Dictionary<string, string> { ["es"] = "Capacidad", ["en"] = "Capacity" }),
            AttributeKind.Number);
        var handler = new ReviewProductHandler(
            new InMemoryProductCatalogReader(product), new DefinitionsOf(litres), new Categories());

        var review = await handler.HandleAsync(new ReviewProductQuery(product.Code, "en"), TestContext.Current.CancellationToken);

        Assert.Equal("Capacity", Assert.Single(review!.Attributes).Label);
    }

    [Fact]
    public async Task Answers_nothing_for_a_code_no_product_has()
    {
        var review = await Handler().HandleAsync(new ReviewProductQuery("NOPE", "es"), TestContext.Current.CancellationToken);

        Assert.Null(review);
    }

    private static ReviewProductHandler Handler(params Product[] products) =>
        new(new InMemoryProductCatalogReader(products), new NoDefinitions(), new Categories());

    private sealed class NoDefinitions : IAttributeDefinitionReader
    {
        public Task<AttributeDefinitions> AllAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(AttributeDefinitions.Empty);
    }

    private sealed class DefinitionsOf(params AttributeDefinition[] definitions) : IAttributeDefinitionReader
    {
        public Task<AttributeDefinitions> AllAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new AttributeDefinitions(definitions));
    }

    private sealed class Categories : ICategoryReader
    {
        public Task<CategoryTree> AllAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new CategoryTree(
            [
                Category.Define(Clock, "CLOTHING",
                    new LocalizedText(new Dictionary<string, string> { ["es"] = "Ropa", ["en"] = "Clothing" })),
            ]));
    }
}
