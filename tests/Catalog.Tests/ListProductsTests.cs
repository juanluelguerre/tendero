using ElGuerre.Tendero.Catalog.Domain;
using ElGuerre.Tendero.Catalog.Features.ListProducts;
using ElGuerre.Tendero.Catalog.Ports;
using ElGuerre.Tendero.SharedKernel;
using Xunit;

using ElGuerre.Tendero.Tests;

namespace ElGuerre.Tendero.Catalog.Tests.Features;

/// <summary>
/// Without this query the review queue cannot exist: the API had five endpoints
/// and none of them listed anything, so the only way to know what was in Draft
/// was to open Postgres.
///
/// What is checked here is the query's contract — filter, paging and language
/// resolution — not the SQL, which belongs to the adapter.
/// </summary>
public sealed class ListProductsTests
{
    private static readonly TestClock Clock = new();

    [Fact]
    public async Task Listing_filters_by_status()
    {
        var draft = ADraftProduct("Cafetera");
        var active = ADraftProduct("Mochila");
        active.Publish(Clock);

        var result = await HandlerOver(draft, active).HandleAsync(
            new ListProductsQuery("draft", "es", 1, 20), TestContext.Current.CancellationToken);

        Assert.Equal(1, result.Total);
        Assert.Equal(draft.Id.Value.ToString(), Assert.Single(result.Items).ProductId);
    }

    [Fact]
    public async Task Listing_without_a_status_returns_the_whole_catalogue()
    {
        var draft = ADraftProduct("Cafetera");
        var active = ADraftProduct("Mochila");
        active.Publish(Clock);

        var result = await HandlerOver(draft, active).HandleAsync(
            new ListProductsQuery(null, "es", 1, 20), TestContext.Current.CancellationToken);

        Assert.Equal(2, result.Total);
    }

    [Fact]
    public async Task Listing_resolves_the_name_in_the_requested_culture()
    {
        var product = Product.Create(Clock, 
            new LocalizedText(new Dictionary<string, string> { ["es"] = "Cafetera", ["en"] = "Coffee maker" }),
            new Money(29.90m, "EUR"));

        var spanish = await HandlerOver(product).HandleAsync(
            new ListProductsQuery(null, "es", 1, 20), TestContext.Current.CancellationToken);
        var english = await HandlerOver(product).HandleAsync(
            new ListProductsQuery(null, "en", 1, 20), TestContext.Current.CancellationToken);

        Assert.Equal("Cafetera", spanish.Items[0].Name);
        Assert.Equal("Coffee maker", english.Items[0].Name);
    }

    [Fact]
    public async Task Total_counts_the_whole_match_not_just_the_page()
    {
        // If total counted only the page, the UI could not draw the pager or say
        // how many are left to review, which is the number a queue is about.
        var products = Enumerable.Range(0, 5).Select(i => ADraftProduct($"Producto {i}")).ToArray();

        var result = await HandlerOver(products).HandleAsync(
            new ListProductsQuery("draft", "es", 1, 2), TestContext.Current.CancellationToken);

        Assert.Equal(2, result.Items.Count);
        Assert.Equal(5, result.Total);
    }

    [Theory]
    [InlineData("draft")]
    [InlineData("DRAFT")]
    [InlineData("active")]
    [InlineData("archived")]
    [InlineData(null)]
    public void Accepted_statuses_validate(string? status) =>
        Assert.True(new ListProductsValidator()
            .Validate(new ListProductsQuery(status, "es", 1, 20)).IsValid);

    [Fact]
    public void An_unknown_status_is_rejected_instead_of_silently_ignored() =>
        Assert.False(new ListProductsValidator()
            .Validate(new ListProductsQuery("pendiente", "es", 1, 20)).IsValid);

    private static ListProductsHandler HandlerOver(params Product[] products) =>
        new(new InMemoryProductCatalogReader(products));

    private static Product ADraftProduct(string name) =>
        Product.Create(Clock, LocalizedText.From("es", name), new Money(29.90m, "EUR"));

    private sealed class InMemoryProductCatalogReader(params Product[] products) : IProductCatalogReader
    {
        public Task<ProductPage> ListAsync(
            ProductStatus? status, int page, int pageSize, CancellationToken ct)
        {
            var matching = products.Where(p => status is null || p.Status == status).ToList();

            return Task.FromResult(new ProductPage(
                [.. matching.Skip((page - 1) * pageSize).Take(pageSize)],
                matching.Count));
        }
    }
}
