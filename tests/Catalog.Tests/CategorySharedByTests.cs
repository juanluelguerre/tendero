using ElGuerre.Tendero.Catalog.Domain;
using ElGuerre.Tendero.SharedKernel;
using ElGuerre.Tendero.Tests;
using Xunit;

namespace ElGuerre.Tendero.Catalog.Tests;

/// <summary>
/// The category a group of products shares (phase 16, P16-11): the deepest one
/// every product is in, itself or below it. A store's collection becomes a
/// category this way — "Cocina" holds cookware and small appliances, and both
/// are kitchen — so its old address can be sent to the category's page.
/// </summary>
public sealed class CategorySharedByTests
{
    private static readonly TestClock Clock = new();

    private static LocalizedText Text(string es, string en) => LocalizedText.From("es", es).With("en", en);

    private static CategoryTree Tree()
    {
        var home = Category.Define(Clock, "HOME", Text("Hogar", "Home"));
        var kitchen = Category.Define(Clock, "KITCHEN", Text("Cocina", "Kitchen"), home);
        var cookware = Category.Define(Clock, "COOKWARE", Text("Menaje", "Cookware"), kitchen);
        var appliances = Category.Define(Clock, "APPLIANCES", Text("Electrodomésticos", "Appliances"), kitchen);
        var gifts = Category.Define(Clock, "GIFTS", Text("Regalos", "Gifts"));
        return new CategoryTree([home, kitchen, cookware, appliances, gifts]);
    }

    [Fact]
    public void Products_of_one_category_share_it()
    {
        Assert.Equal("COOKWARE", Tree().SharedBy(["COOKWARE", "cookware"]));
    }

    [Fact]
    public void Products_of_sibling_categories_share_their_parent()
    {
        Assert.Equal("KITCHEN", Tree().SharedBy(["COOKWARE", "APPLIANCES"]));
    }

    [Fact]
    public void A_product_of_a_category_and_one_below_it_share_the_upper_one()
    {
        Assert.Equal("KITCHEN", Tree().SharedBy(["KITCHEN", "COOKWARE"]));
    }

    /// <summary>
    /// Nothing shared, nothing to say: a collection of cookware and gift cards is
    /// no category's page, and sending it to one would be a guess.
    /// </summary>
    [Theory]
    [InlineData("COOKWARE", "GIFTS")]
    [InlineData("COOKWARE", null)]
    [InlineData("COOKWARE", "NOT_A_CATEGORY")]
    public void Products_with_no_category_in_common_share_none(string? one, string? other)
    {
        Assert.Null(Tree().SharedBy([one, other]));
    }

    [Fact]
    public void No_products_share_nothing()
    {
        Assert.Null(Tree().SharedBy([]));
    }
}
