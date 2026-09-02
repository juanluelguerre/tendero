using ElGuerre.Tendero.Catalog.Domain;
using ElGuerre.Tendero.SharedKernel;
using ElGuerre.Tendero.Tests;
using Xunit;

namespace ElGuerre.Tendero.Catalog.Tests.Domain;

public sealed class VariantTests
{
    private static readonly TestClock Clock = new();

    private static Product AShirt()
    {
        var product = Product.Create(
            Clock, LocalizedText.From("es", "Camiseta técnica"), new Money(24.90m, "EUR"));
        product.DefineAxes(Clock, ["COLOR", "SIZE"]);
        return product;
    }

    [Fact]
    public void A_variant_is_added_with_its_axis_values()
    {
        var product = AShirt();

        var variant = product.AddVariant(
            Clock, "SHIRT-NAVY-M", new Money(24.90m, "EUR"),
            new Dictionary<string, string> { ["COLOR"] = "NAVY", ["SIZE"] = "M" });

        Assert.Equal("SHIRT-NAVY-M", variant.Sku);
        Assert.Equal("NAVY", variant.AxisValues["COLOR"]);
        Assert.Same(variant, product.VariantBySku("shirt-navy-m"));
    }

    /// <summary>
    /// Re-importing cannot duplicate. It is the same rule <c>LinkExternal</c> and
    /// <c>AddImage</c> already honour: everything a connector can send twice is
    /// idempotent by identity.
    /// </summary>
    [Fact]
    public void Adding_the_same_sku_twice_returns_the_first_one()
    {
        var product = AShirt();
        var values = new Dictionary<string, string> { ["COLOR"] = "NAVY", ["SIZE"] = "M" };

        var first = product.AddVariant(Clock, "SHIRT-NAVY-M", new Money(24.90m, "EUR"), values);
        var again = product.AddVariant(Clock, "SHIRT-NAVY-M", new Money(99.00m, "EUR"), values);

        Assert.Same(first, again);
        Assert.Single(product.Variants);
    }

    /// <summary>
    /// Two variants with the same coordinates are one variant with two SKUs, and
    /// that turns the PDP's picker into a lottery.
    /// </summary>
    [Fact]
    public void Two_variants_cannot_share_the_same_axis_values()
    {
        var product = AShirt();
        product.AddVariant(Clock, "SHIRT-NAVY-M", new Money(24.90m, "EUR"),
            new Dictionary<string, string> { ["COLOR"] = "NAVY", ["SIZE"] = "M" });

        Assert.Throws<InvalidOperationException>(() =>
            product.AddVariant(Clock, "SHIRT-NAVY-M-DUP", new Money(24.90m, "EUR"),
                new Dictionary<string, string> { ["COLOR"] = "NAVY", ["SIZE"] = "M" }));
    }

    [Fact]
    public void An_undeclared_axis_is_rejected_and_says_which_ones_exist()
    {
        var product = AShirt();

        var error = Assert.Throws<InvalidOperationException>(() =>
            product.AddVariant(Clock, "SHIRT-XL", new Money(24.90m, "EUR"),
                new Dictionary<string, string> { ["MATERIAL"] = "COTTON" }));

        Assert.Contains("MATERIAL", error.Message, StringComparison.Ordinal);
        Assert.Contains("COLOR", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Changing the axes with live variants would leave each of them described by
    /// coordinates that no longer mean the same thing.
    /// </summary>
    [Fact]
    public void Axes_cannot_change_while_variants_exist()
    {
        var product = AShirt();
        product.AddVariant(Clock, "SHIRT-NAVY-M", new Money(24.90m, "EUR"),
            new Dictionary<string, string> { ["COLOR"] = "NAVY", ["SIZE"] = "M" });

        Assert.Throws<InvalidOperationException>(() => product.DefineAxes(Clock, ["SIZE", "COLOR"]));
    }

    [Fact]
    public void The_price_range_spans_the_available_variants()
    {
        var product = AShirt();
        product.AddVariant(Clock, "SHIRT-NAVY-M", new Money(24.90m, "EUR"),
            new Dictionary<string, string> { ["COLOR"] = "NAVY", ["SIZE"] = "M" });
        product.AddVariant(Clock, "SHIRT-NAVY-L", new Money(29.90m, "EUR"),
            new Dictionary<string, string> { ["COLOR"] = "NAVY", ["SIZE"] = "L" });

        var (from, to) = product.PriceRange;

        Assert.Equal(24.90m, from.Amount);
        Assert.Equal(29.90m, to.Amount);
    }

    /// <summary>
    /// A retired variant cannot go on setting the "from" price: the card would
    /// advertise a price nobody can buy.
    /// </summary>
    [Fact]
    public void A_discontinued_variant_leaves_the_price_range()
    {
        var product = AShirt();
        product.AddVariant(Clock, "SHIRT-NAVY-M", new Money(24.90m, "EUR"),
            new Dictionary<string, string> { ["COLOR"] = "NAVY", ["SIZE"] = "M" });
        product.AddVariant(Clock, "SHIRT-NAVY-L", new Money(29.90m, "EUR"),
            new Dictionary<string, string> { ["COLOR"] = "NAVY", ["SIZE"] = "L" });

        product.DiscontinueVariant(Clock, "SHIRT-NAVY-M");

        var (from, to) = product.PriceRange;

        Assert.Equal(29.90m, from.Amount);
        Assert.Equal(29.90m, to.Amount);
    }

    /// <summary>
    /// The product fixes the order, not the dictionary: a dictionary cannot say
    /// whether the label reads "azul marino · 38" or the other way round.
    /// </summary>
    [Fact]
    public void The_label_follows_the_declared_axis_order()
    {
        var product = AShirt();
        var variant = product.AddVariant(Clock, "SHIRT-NAVY-M", new Money(24.90m, "EUR"),
            new Dictionary<string, string> { ["SIZE"] = "M", ["COLOR"] = "NAVY" });

        Assert.Equal("NAVY · M", variant.LabelFor(product.VariantAxes));
    }

    [Fact]
    public void A_product_with_no_variants_still_reports_a_price_range()
    {
        var product = Product.Create(
            Clock, LocalizedText.From("es", "Cafetera"), new Money(29.90m, "EUR"));

        var (from, to) = product.PriceRange;

        Assert.Equal(29.90m, from.Amount);
        Assert.Equal(29.90m, to.Amount);
    }
}
