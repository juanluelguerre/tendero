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

    /// <summary>
    /// The defect the UI review of 2026-09-11 found on the six original
    /// products: the seed gained an illustration for each, a re-import ADDED it
    /// behind the old placeholder, and the placeholder stayed the cover forever,
    /// because the cover is the lowest sort order and appending never takes it.
    /// A gallery that comes from a source is the source's, in the source's order.
    /// </summary>
    [Fact]
    public void A_gallery_that_replaces_another_puts_its_first_image_on_the_cover()
    {
        var product = AProduct();
        product.AddImage(Clock, new ImageId("placeholder"));

        product.ReplaceImages(Clock, [(new ImageId("illustration"), null), (new ImageId("placeholder"), null)]);

        Assert.Equal(new ImageId("illustration"), product.PrimaryImage?.Id);
        Assert.Equal(["illustration", "placeholder"], product.Images.OrderBy(i => i.SortOrder).Select(i => i.Id.Value));
    }

    [Fact]
    public void An_image_the_source_no_longer_lists_leaves_the_gallery()
    {
        var product = AProduct();
        product.AddImage(Clock, new ImageId("aaa"));
        product.AddImage(Clock, new ImageId("bbb"));

        product.ReplaceImages(Clock, [(new ImageId("bbb"), null)]);

        Assert.Equal(new ImageId("bbb"), Assert.Single(product.Images).Id);
    }

    /// <summary>Re-importing a product whose gallery did not change is not a
    /// change: no new timestamp and no event, or every import would reindex the
    /// whole catalogue for nothing.</summary>
    [Fact]
    public void Replacing_a_gallery_with_the_same_one_changes_nothing()
    {
        var clock = new TestClock();
        var product = Product.Create(clock, LocalizedText.From("es", "Cafetera"), new Money(29.90m, "EUR"));
        product.AddImage(clock, new ImageId("aaa"), LocalizedText.From("es", "Cafetera de goteo"));
        var events = product.DomainEvents.Count;
        var updated = product.UpdatedAt;

        // A NEW LocalizedText with the same words, which is what every import
        // builds: comparing the alt by reference would call it a change.
        clock.Advance(TimeSpan.FromHours(1));
        product.ReplaceImages(clock, [(new ImageId("aaa"), LocalizedText.From("es", "Cafetera de goteo"))]);

        Assert.Equal(updated, product.UpdatedAt);
        Assert.Equal(events, product.DomainEvents.Count);
    }

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
