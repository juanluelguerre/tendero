using ElGuerre.Tendero.Catalog.Domain;
using ElGuerre.Tendero.SharedKernel;
using ElGuerre.Tendero.Tests;
using Xunit;

namespace ElGuerre.Tendero.Catalog.Tests;

/// <summary>
/// **A picture says what it shows** (`docs/analysis/catalogue-model.md`,
/// proposal 1). A variant used to point at one picture, so a colour had one
/// picture and every size of it repeated the same id. Now a picture of the
/// gallery says which axis values it shows — the colour, usually — and what a
/// variant's pictures are is derived: a colour has as many as say so, every
/// size of it shares them, and a picture that says nothing is the product's.
/// </summary>
public sealed class ProductPicturesOfVariantsTests
{
    private static readonly TestClock Clock = new();
    private static readonly Money Price = new(10m, "EUR");

    private static Dictionary<string, string> Of(string colour, string? size = null) =>
        size is null ? new() { ["COLOR"] = colour } : new() { ["COLOR"] = colour, ["SIZE"] = size };

    /// <summary>A shoe in navy and black, each in two sizes, with five pictures in its gallery and none tagged yet.</summary>
    private static Product Shoe()
    {
        var shoe = Product.Create(Clock, LocalizedText.From("es", "Zapatilla").With("en", "Shoe"), Price);
        shoe.DefineAxes(Clock, ["COLOR", "SIZE"]);
        foreach (var colour in new[] { "NAVY_BLUE", "BLACK" })
            foreach (var size in new[] { "38", "39" })
                shoe.AddVariant(Clock, $"SHOE-{colour}-{size}", Price, Of(colour, size));
        foreach (var picture in new[] { "navy-side", "black-side", "sole", "navy-top", "box" })
            shoe.AddImage(Clock, new ImageId(picture));
        return shoe;
    }

    private static string[] PicturesOf(Product product, string sku) =>
        [.. product.PicturesOf(product.Variants.Single(variant => variant.Sku == sku)).Select(image => image.Id.Value)];

    [Fact]
    public void A_picture_is_told_which_colour_it_shows_and_says_so()
    {
        var shoe = Shoe();

        Assert.True(shoe.Show(Clock, new ImageId("navy-side"), Of("navy_blue")));

        var picture = shoe.Images.Single(image => image.Id.Value == "navy-side");
        Assert.Equal([new AxisValue("COLOR", "NAVY_BLUE")], picture.Shows);
        Assert.All(shoe.Images.Where(image => image.Id.Value != "navy-side"), image => Assert.Empty(image.Shows));
    }

    /// <summary>
    /// A colour's pictures are the ones that say they show it, in the gallery's
    /// order, and then the ones that say nothing — the sole and the box are
    /// every colour's. Every size of the colour has the same.
    /// </summary>
    [Fact]
    public void A_variants_pictures_are_those_that_show_its_colour_then_those_that_show_nothing()
    {
        var shoe = Shoe();
        shoe.Show(Clock, new ImageId("navy-side"), Of("NAVY_BLUE"));
        shoe.Show(Clock, new ImageId("navy-top"), Of("NAVY_BLUE"));
        shoe.Show(Clock, new ImageId("black-side"), Of("BLACK"));

        Assert.Equal(["navy-side", "navy-top", "sole", "box"], PicturesOf(shoe, "SHOE-NAVY_BLUE-38"));
        Assert.Equal(PicturesOf(shoe, "SHOE-NAVY_BLUE-38"), PicturesOf(shoe, "SHOE-NAVY_BLUE-39"));
        Assert.Equal(["black-side", "sole", "box"], PicturesOf(shoe, "SHOE-BLACK-39"));
    }

    /// <summary>The one a cart line and a search result show: the first of its own, or the cover when no picture is its own.</summary>
    [Fact]
    public void A_variants_picture_is_the_first_of_its_own_and_the_cover_when_it_has_none()
    {
        var shoe = Shoe();
        var (navy, black) = (shoe.Variants.First(variant => variant.Sku.Contains("NAVY", StringComparison.Ordinal)), shoe.Variants.First(variant => variant.Sku.Contains("BLACK", StringComparison.Ordinal)));

        // Nothing tagged: every variant has the cover.
        Assert.Equal((shoe.PrimaryImage!.Id, shoe.PrimaryImage.Id), (shoe.PictureOf(navy)!.Id, shoe.PictureOf(black)!.Id));

        shoe.Show(Clock, new ImageId("black-side"), Of("BLACK"));

        Assert.Equal("black-side", shoe.PictureOf(black)!.Id.Value);
        // The cover, navy-side, is now nobody's in particular and still the first untagged.
        Assert.Equal("navy-side", shoe.PictureOf(navy)!.Id.Value);
    }

    /// <summary>Every picture is another colour's: the variant still has a picture, the cover, which is still the product.</summary>
    [Fact]
    public void A_variant_whose_colour_has_no_picture_when_every_picture_is_another_colours_has_the_cover()
    {
        var shoe = Shoe();
        foreach (var image in shoe.Images.ToList())
            shoe.Show(Clock, image.Id, Of("BLACK"));
        var navy = shoe.Variants.First(variant => variant.Sku.Contains("NAVY", StringComparison.Ordinal));

        Assert.Empty(shoe.PicturesOf(navy));
        Assert.Equal(shoe.PrimaryImage!.Id, shoe.PictureOf(navy)!.Id);
    }

    [Fact]
    public void A_product_with_no_pictures_has_none_for_a_variant()
    {
        var mug = Product.Create(Clock, LocalizedText.From("es", "Taza"), Price);
        var only = mug.AddVariant(Clock, "MUG-DEFAULT", Price);

        Assert.Null(mug.PictureOf(only));
        Assert.Empty(mug.PicturesOf(only));
    }

    /// <summary>A picture tagged with a colour and a size is that one variant's: the tag is satisfied by coordinates, whatever the axes.</summary>
    [Fact]
    public void A_picture_may_show_a_value_of_more_than_one_axis_and_is_then_only_those_variants()
    {
        var shoe = Shoe();
        shoe.Show(Clock, new ImageId("navy-top"), Of("NAVY_BLUE", "39"));

        Assert.Contains("navy-top", PicturesOf(shoe, "SHOE-NAVY_BLUE-39"));
        Assert.DoesNotContain("navy-top", PicturesOf(shoe, "SHOE-NAVY_BLUE-38"));
    }

    /// <summary>
    /// What a tag may say is what the product varies by and somebody sells: an
    /// axis it does not have, or a colour no variant holds, would be a picture
    /// that shows on nothing.
    /// </summary>
    [Fact]
    public void A_picture_cannot_show_an_axis_the_product_does_not_have_or_a_value_no_variant_holds()
    {
        var shoe = Shoe();

        Assert.Throws<InvalidOperationException>(() => shoe.Show(Clock, new ImageId("sole"), new Dictionary<string, string> { ["MATERIAL"] = "LEATHER" }));
        Assert.Throws<InvalidOperationException>(() => shoe.Show(Clock, new ImageId("sole"), Of("OLIVE")));
        Assert.Empty(shoe.Images.Single(image => image.Id.Value == "sole").Shows);
    }

    [Fact]
    public void Showing_nothing_clears_the_tag_and_an_unknown_picture_is_not_tagged()
    {
        var shoe = Shoe();
        shoe.Show(Clock, new ImageId("navy-side"), Of("NAVY_BLUE"));

        Assert.True(shoe.Show(Clock, new ImageId("navy-side"), new Dictionary<string, string>()));
        Assert.False(shoe.Show(Clock, new ImageId("not-in-the-gallery"), Of("NAVY_BLUE")));

        Assert.All(shoe.Images, image => Assert.Empty(image.Shows));
    }

    /// <summary>The same tag again is not a change: no timestamp, or every import would reindex the product.</summary>
    [Fact]
    public void The_same_tag_again_is_not_a_change()
    {
        var shoe = Shoe();
        shoe.Show(Clock, new ImageId("navy-side"), Of("NAVY_BLUE"));
        var before = shoe.UpdatedAt;
        var later = new TestClock();
        later.Advance(TimeSpan.FromHours(1));

        // And it is still a yes: the picture exists and shows what was said. False is "no such picture".
        Assert.True(shoe.Show(later, new ImageId("navy-side"), Of("navy_blue")));

        Assert.Equal(before, shoe.UpdatedAt);
    }

    /// <summary>The same sentence again is the same answer, as the tag's is: the picture is there and says it.</summary>
    [Fact]
    public void The_same_description_again_is_still_a_yes_and_not_a_change()
    {
        var shoe = Shoe();
        var words = LocalizedText.From("es", "Zapatilla azul, de lado").With("en", "Navy shoe, side view");
        Assert.True(shoe.DescribeImage(Clock, new ImageId("navy-side"), words));
        var before = shoe.UpdatedAt;
        var later = new TestClock();
        later.Advance(TimeSpan.FromHours(1));

        Assert.True(shoe.DescribeImage(later, new ImageId("navy-side"), words));
        Assert.False(shoe.DescribeImage(later, new ImageId("never-there"), words));

        Assert.Equal(before, shoe.UpdatedAt);
    }

    /// <summary>
    /// The last variant of a colour is discontinued: a picture that shows that
    /// colour would now show on nothing, so it goes back to being the product's.
    /// While one size of the colour is still sold, the tag stays.
    /// </summary>
    [Fact]
    public void A_picture_of_a_colour_nobody_sells_any_more_goes_back_to_showing_nothing()
    {
        var shoe = Shoe();
        shoe.Show(Clock, new ImageId("black-side"), Of("BLACK"));

        shoe.DiscontinueVariant(Clock, "SHOE-BLACK-38");
        Assert.NotEmpty(shoe.Images.Single(image => image.Id.Value == "black-side").Shows);

        shoe.DiscontinueVariant(Clock, "SHOE-BLACK-39");
        Assert.Empty(shoe.Images.Single(image => image.Id.Value == "black-side").Shows);
    }

    /// <summary>The source's gallery replaces the product's (ADR 0011) and says nothing of tags: a picture that stays keeps what it shows.</summary>
    [Fact]
    public void A_gallery_replaced_by_the_source_keeps_what_its_pictures_show()
    {
        var shoe = Shoe();
        shoe.Show(Clock, new ImageId("black-side"), Of("BLACK"));

        shoe.ReplaceImages(Clock, [(new ImageId("black-side"), null), (new ImageId("navy-side"), null)]);

        Assert.Equal([new AxisValue("COLOR", "BLACK")], shoe.Images.Single(image => image.Id.Value == "black-side").Shows);
        Assert.Equal(["black-side", "navy-side"], shoe.Images.OrderBy(image => image.SortOrder).Select(image => image.Id.Value));
    }

    /// <summary>A picture the source brings for the first time shows nothing yet — an empty list, never a missing one, so it is every variant's.</summary>
    [Fact]
    public void A_picture_new_to_the_gallery_shows_nothing_and_is_every_variants()
    {
        var shoe = Shoe();

        shoe.ReplaceImages(Clock, [(new ImageId("navy-side"), null), (new ImageId("lifestyle"), null)]);

        var brought = shoe.Images.Single(image => image.Id.Value == "lifestyle");
        Assert.NotNull(brought.Shows);
        Assert.Empty(brought.Shows);
        Assert.Contains("lifestyle", PicturesOf(shoe, "SHOE-BLACK-38"));
    }
}
