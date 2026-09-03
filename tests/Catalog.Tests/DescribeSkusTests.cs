using ElGuerre.Tendero.Catalog.Domain;
using ElGuerre.Tendero.Catalog.Features.DescribeSkus;
using ElGuerre.Tendero.SharedKernel;
using ElGuerre.Tendero.Tests;
using Xunit;

namespace ElGuerre.Tendero.Catalog.Tests.Features;

/// <summary>
/// What a set of SKUs is called.
///
/// The slice exists because of a boundary: `Inventory` may reference the
/// SharedKernel and nothing else, so a stock grid can show `B05DEFG606-DEFAULT`
/// and cannot show what it is. The catalogue answers that question separately
/// and the screen puts the two together.
/// </summary>
public sealed class DescribeSkusTests
{
    private static readonly TestClock Clock = new();

    [Fact]
    public async Task A_sku_is_described_with_its_product_and_variant()
    {
        var shirt = AShirt();

        var result = await HandlerOver(shirt).HandleAsync(
            new DescribeSkusQuery(["SHIRT-NAVY-38"], "es"), TestContext.Current.CancellationToken);

        var described = Assert.Single(result.Items);
        Assert.Equal("SHIRT-NAVY-38", described.Sku);
        Assert.Equal("Camisa de lino", described.ProductName);
        Assert.Equal("NAVY_BLUE · 38", described.VariantLabel);
        Assert.Equal(shirt.Code, described.ProductCode);
    }

    /// <summary>
    /// The label is the option CODES, not their translations, and that is the
    /// right answer for a backoffice: the box on the shelf is labelled
    /// `PULSE-NAVY_BLUE-38`, so a row reading "NAVY_BLUE · 38" matches what a
    /// shopkeeper is holding. The storefront resolves the same axes into "azul
    /// marino · 38" because a shopper is not holding a box.
    /// </summary>
    [Fact]
    public async Task The_label_matches_what_is_printed_on_the_box()
    {
        var result = await HandlerOver(AShirt()).HandleAsync(
            new DescribeSkusQuery(["SHIRT-NAVY-38"], "en"), TestContext.Current.CancellationToken);

        Assert.Equal("NAVY_BLUE · 38", Assert.Single(result.Items).VariantLabel);
    }

    /// <summary>The product name IS translated, because it is the thing the
    /// shopkeeper is looking for and they read it in their own language.</summary>
    [Fact]
    public async Task The_product_name_answers_in_the_requested_culture()
    {
        var result = await HandlerOver(AShirt()).HandleAsync(
            new DescribeSkusQuery(["SHIRT-NAVY-38"], "en"), TestContext.Current.CancellationToken);

        Assert.Equal("Linen shirt", Assert.Single(result.Items).ProductName);
    }

    [Fact]
    public async Task Several_skus_are_answered_in_one_call()
    {
        var result = await HandlerOver(AShirt()).HandleAsync(
            new DescribeSkusQuery(["SHIRT-NAVY-38", "SHIRT-BLACK-38"], "es"),
            TestContext.Current.CancellationToken);

        Assert.Equal(
            ["SHIRT-BLACK-38", "SHIRT-NAVY-38"],
            result.Items.Select(item => item.Sku).Order());
    }

    /// <summary>
    /// A SKU the catalogue has never heard of is ABSENT from the answer, not a
    /// row with an empty name.
    ///
    /// It is a real state and not a defensive check: the rule that lets
    /// Inventory ignore Catalog also lets stock OUTLIVE a product. A shelf
    /// holding something the catalogue no longer lists is exactly the row a
    /// shopkeeper needs to see flagged, and "this product is called nothing" is
    /// a different and less true statement than "the catalogue does not know
    /// this".
    /// </summary>
    [Fact]
    public async Task A_sku_the_catalogue_never_heard_of_is_absent_rather_than_empty()
    {
        var result = await HandlerOver(AShirt()).HandleAsync(
            new DescribeSkusQuery(["SHIRT-NAVY-38", "GHOST-SKU-1"], "es"),
            TestContext.Current.CancellationToken);

        Assert.Equal(["SHIRT-NAVY-38"], result.Items.Select(item => item.Sku));
    }

    /// <summary>
    /// The implicit default variant has no coordinates, so there is nothing to
    /// tell it apart from — and null says that better than an empty string,
    /// which a template would render as a stray separator.
    /// </summary>
    [Fact]
    public async Task A_product_with_one_default_variant_has_no_label()
    {
        var product = Product.Create(
            Clock, LocalizedText.From("es", "Cafetera"), new Money(29.90m, "EUR"));
        product.AddVariant(Clock, "COFFEE-DEFAULT", new Money(29.90m, "EUR"));

        var result = await HandlerOver(product).HandleAsync(
            new DescribeSkusQuery(["COFFEE-DEFAULT"], "es"), TestContext.Current.CancellationToken);

        Assert.Null(Assert.Single(result.Items).VariantLabel);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(201)]
    public void The_request_is_bounded_at_both_ends(int count)
    {
        var query = new DescribeSkusQuery([.. Enumerable.Range(0, count).Select(i => $"SKU-{i}")], "es");

        Assert.False(new DescribeSkusValidator().Validate(query).IsValid);
    }

    private static DescribeSkusHandler HandlerOver(Product product) =>
        new(new InMemoryProductCatalogReader(product));

    private static Product AShirt()
    {
        var product = Product.Create(
            Clock,
            new LocalizedText(new Dictionary<string, string>
            {
                ["es"] = "Camisa de lino",
                ["en"] = "Linen shirt"
            }),
            new Money(29.90m, "EUR"));

        product.DefineAxes(Clock, ["COLOR", "SIZE"]);
        product.AddVariant(Clock, "SHIRT-NAVY-38", new Money(29.90m, "EUR"),
            new Dictionary<string, string> { ["COLOR"] = "NAVY_BLUE", ["SIZE"] = "38" });
        product.AddVariant(Clock, "SHIRT-BLACK-38", new Money(29.90m, "EUR"),
            new Dictionary<string, string> { ["COLOR"] = "BLACK", ["SIZE"] = "38" });

        product.Publish(Clock);
        return product;
    }
}
