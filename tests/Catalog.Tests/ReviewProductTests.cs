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
        Assert.Equal("draft", review.Status);
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
