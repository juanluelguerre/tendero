using ElGuerre.Tendero.Catalog.Domain;
using ElGuerre.Tendero.SharedKernel;
using ElGuerre.Tendero.Tests;
using Xunit;

namespace ElGuerre.Tendero.Catalog.Tests.Domain;

/// <summary>
/// The aggregate's two rules that used to be computed outside it, each with its
/// own version: which photo is the cover, and how a slug is written.
/// </summary>
public sealed class ProductTests
{
    private static readonly TestClock Clock = new();

    /// <summary>
    /// The reason the clock is a parameter and not DateTimeOffset.UtcNow: without
    /// this, any assertion about CreatedAt/UpdatedAt can only check that the
    /// stamp "is recent", which is an elegant way of checking nothing. The
    /// time-bounded rules that are coming — promotion validity, mandate expiry,
    /// the returns window — depend on this being exact.
    /// </summary>
    [Fact]
    public void Timestamps_come_from_the_clock_and_not_from_the_wall()
    {
        var clock = new TestClock();

        var product = Product.Create(clock, LocalizedText.From("es", "Cafetera"), new Money(29.90m, "EUR"));

        Assert.Equal(TestClock.Default, product.CreatedAt);
        Assert.Equal(TestClock.Default, product.UpdatedAt);

        var later = clock.Advance(TimeSpan.FromHours(3));
        product.Publish(clock);

        Assert.Equal(TestClock.Default, product.CreatedAt);
        Assert.Equal(later, product.UpdatedAt);
    }

    private static Product AProduct(string name = "Cafetera") =>
        Product.Create(Clock, LocalizedText.From("es", name), new Money(29.90m, "EUR"));

    [Fact]
    public void The_cover_photo_is_the_one_with_the_lowest_sort_order()
    {
        // The list keeps insertion order, so "the first of the list" and "the one
        // with the lowest SortOrder" agree until they do not. The backoffice
        // listing sorted and the search document did not, so the same product
        // could show two different photos.
        var product = AProduct();
        product.AddImage(Clock, new ImageId("aaa"));
        product.AddImage(Clock, new ImageId("bbb"));

        Assert.Equal(new ImageId("aaa"), product.PrimaryImage?.Id);
        Assert.Equal(0, product.PrimaryImage?.SortOrder);
    }

    [Fact]
    public void A_product_with_no_images_has_no_cover_photo() =>
        Assert.Null(AProduct().PrimaryImage);

    [Fact]
    public void Adding_the_same_image_twice_does_not_duplicate_it()
    {
        var product = AProduct();
        product.AddImage(Clock, new ImageId("aaa"));
        product.AddImage(Clock, new ImageId("aaa"));

        Assert.Single(product.Images);
    }

    [Theory]
    // Splitting on spaces alone left brackets and accents inside the URL.
    [InlineData("Cafetera Espresso (12 tazas)", "cafetera-espresso-12-tazas")]
    [InlineData("Zapatillas de running — mujer", "zapatillas-de-running-mujer")]
    [InlineData("Mochila 25L", "mochila-25l")]
    [InlineData("  espacios   de   sobra  ", "espacios-de-sobra")]
    public void A_slug_only_carries_letters_digits_and_hyphens(string name, string expected) =>
        Assert.Equal(expected, AProduct(name).Slug.In("es"));

    [Theory]
    [InlineData("Café con leche ñiño", "cafe-con-leche-nino")]
    [InlineData("Ánfora Gütiérrez", "anfora-gutierrez")]
    public void Accents_are_folded_rather_than_escaped(string name, string expected)
    {
        // Folded with an explicit table, not with Normalize(FormD): the repo
        // builds with InvariantGlobalization=true, where Unicode normalisation
        // returns the string intact without throwing. This test is what proved it.
        Assert.Equal(expected, AProduct(name).Slug.In("es"));
    }

    [Fact]
    public void Every_culture_gets_its_own_slug()
    {
        var product = Product.Create(Clock,
            new LocalizedText(new Dictionary<string, string>
            {
                ["es"] = "Cafetera de goteo",
                ["en"] = "Drip coffee maker"
            }),
            new Money(29.90m, "EUR"));

        Assert.Equal("cafetera-de-goteo", product.Slug.In("es"));
        Assert.Equal("drip-coffee-maker", product.Slug.In("en"));
    }

    [Fact]
    public void Creating_a_product_is_one_event_not_two()
    {
        // Brand and category go into Create. When they arrived in a later
        // UpdateDetails, creating a product emitted two ProductUpserted, and the
        // indexing worker wrote the same document twice.
        var product = Product.Create(Clock,
            LocalizedText.From("es", "Cafetera"),
            new Money(29.90m, "EUR"),
            description: null,
            brand: "Moka",
            category: "COFFEE_MAKER");

        Assert.Equal("Moka", product.Brand);
        Assert.Equal("COFFEE_MAKER", product.Category);
        Assert.Single(product.DomainEvents);
    }
}
