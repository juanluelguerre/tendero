using ElGuerre.Tendero.Catalog.Domain;
using ElGuerre.Tendero.Catalog.Features.ListProducts;
using ElGuerre.Tendero.Catalog.Ports;
using ElGuerre.Tendero.SharedKernel;
using ElGuerre.Tendero.Tests;
using Xunit;

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
            new ListProductsQuery(ProductStatus.Draft, "es", 1, 20), TestContext.Current.CancellationToken);

        Assert.Equal(1, result.Total);
        Assert.Equal(draft.Id.Value.ToString(), Assert.Single(result.Items).ProductId);
    }

    /// <summary>
    /// Every status says its number whichever one is shown, and zero when it has
    /// none: the filter chips kept their width only while the selected one alone
    /// had a count, and the search box beside them jumped on every click.
    /// </summary>
    [Fact]
    public async Task Counts_every_status_for_the_text_whichever_is_shown()
    {
        var draft = ADraftProduct("Cafetera");
        var active = ADraftProduct("Cafetera italiana");
        active.Publish(Clock);
        var other = ADraftProduct("Mochila");

        var result = await HandlerOver(draft, active, other).HandleAsync(
            new ListProductsQuery(ProductStatus.Active, "es", 1, 20, Text: "cafetera"), TestContext.Current.CancellationToken);

        Assert.Equal(Enum.GetValues<ProductStatus>(), result.Counts.Select(count => count.Status));
        Assert.Equal(1, result.Counts.Single(count => count.Status == ProductStatus.Active).Count);
        Assert.Equal(1, result.Counts.Single(count => count.Status == ProductStatus.Draft).Count);
        Assert.Equal(0, result.Counts.Single(count => count.Status == ProductStatus.Archived).Count);
        Assert.Equal(1, result.Total);
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
            new ListProductsQuery(ProductStatus.Draft, "es", 1, 2), TestContext.Current.CancellationToken);

        Assert.Equal(2, result.Items.Count);
        Assert.Equal(5, result.Total);
    }

    [Theory]
    [InlineData(ProductStatus.Draft)]
    [InlineData(ProductStatus.Active)]
    [InlineData(ProductStatus.Archived)]
    [InlineData(null)]
    public void Accepted_statuses_validate(ProductStatus? status) =>
        Assert.True(new ListProductsValidator()
            .Validate(new ListProductsQuery(status, "es", 1, 20)).IsValid);

    /// <summary>
    /// Over HTTP a name the set does not have never gets this far: the binding
    /// refuses "pendiente" (ADR 0043, held by <c>WireEnumTests</c>). What is
    /// left for the validator is a status built in code from a number.
    /// </summary>
    [Fact]
    public void An_unknown_status_is_rejected_instead_of_silently_ignored() =>
        Assert.False(new ListProductsValidator()
            .Validate(new ListProductsQuery((ProductStatus)99, "es", 1, 20)).IsValid);

    /// <summary>The one paging contract (R-7.2): a page from one, a page size from one to a hundred.</summary>
    [Theory]
    [InlineData(0, 20, false)]
    [InlineData(1, 0, false)]
    [InlineData(1, Paging.MaxPageSize, true)]
    [InlineData(1, Paging.MaxPageSize + 1, false)]
    public void A_page_is_asked_for_within_the_paging_contract(int page, int pageSize, bool valid) =>
        Assert.Equal(valid, new ListProductsValidator().Validate(new ListProductsQuery(null, "es", page, pageSize)).IsValid);

    private static ListProductsHandler HandlerOver(params Product[] products) =>
        new(new InMemoryProductCatalogReader(products));

    private static Product ADraftProduct(string name) =>
        Product.Create(Clock, LocalizedText.From("es", name), new Money(29.90m, "EUR"));

}
