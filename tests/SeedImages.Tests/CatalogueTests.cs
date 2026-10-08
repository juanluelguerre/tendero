using ElGuerre.Tendero.SeedImages;
using Xunit;

namespace ElGuerre.Tendero.SeedImages.Tests;

/// <summary>
/// What the generator has to draw, read from the catalogue rather than from the
/// prose list in <c>seed/IMAGES-TODO.md</c>.
///
/// The two say the same thing today, and the JSON is the one the shop imports.
/// A list typed twice is a list that goes out of date silently — the argument
/// <c>Solution.cs</c> already makes about assemblies, applied to products.
/// </summary>
public sealed class CatalogueTests
{
    private static string Seed => Path.Combine(RepositoryRoot.Find(), "seed", "products.sample.json");
    private static string Attributes => Path.Combine(RepositoryRoot.Find(), "seed", "attributes.sample.json");

    [Fact]
    public void The_catalogue_is_a_hundred_products_and_every_one_can_be_drawn()
    {
        var products = ProductCatalogue.Read(Seed);

        Assert.Equal(100, products.Count(product => product.VariantOf is null));

        // The English description is what the prompt is built from: it names the
        // object without the invented brand, which is what a generator reads best.
        Assert.All(products, product =>
        {
            Assert.False(String.IsNullOrWhiteSpace(product.ItemId));
            Assert.False(String.IsNullOrWhiteSpace(product.EnglishDescription));
        });
    }

    /// <summary>
    /// **Thirty-five products declare no colour at all**, and they are the case a
    /// naive prompt builder gets wrong — it writes "The object is : a bamboo
    /// spice rack" and the model reads a colon as punctuation about nothing.
    ///
    /// The split is asserted rather than the absence, because a seed edit that
    /// moves it is a seed edit that silently changes ninety-two prompts.
    /// </summary>
    [Fact]
    public void Sixty_five_products_declare_a_colour_and_thirty_five_do_not()
    {
        var products = ProductCatalogue.Read(Seed).Where(product => product.VariantOf is null).ToList();

        Assert.Equal(65, products.Count(product => product.SpanishColour is not null));
        Assert.Equal(35, products.Count(product => product.SpanishColour is null));
    }

    /// <summary>
    /// A product sold in two colours has a picture of each (phase 13a, I-4d):
    /// its cover, drawn in the colour of the variant that shows it, and one more
    /// to draw for the other — named for its file, in the other's colour.
    /// </summary>
    [Fact]
    public void A_product_in_two_colours_is_its_cover_and_one_more_picture_in_the_other_colour()
    {
        var products = ProductCatalogue.Read(Seed);

        var cover = products.Single(product => product.ItemId == "B13BAG0711");
        var other = Assert.Single(products, product => product.VariantOf is not null);

        Assert.Equal(("oliva", (string?)null), (cover.SpanishColour, cover.VariantOf));
        Assert.Equal(("B13BAG0711-BLACK", "negro", "B13BAG0711"), (other.ItemId, other.SpanishColour, other.VariantOf));
        Assert.Equal((cover.EnglishName, cover.EnglishDescription), (other.EnglishName, other.EnglishDescription));
    }

    [Fact]
    public void Every_colour_the_catalogue_declares_has_an_english_word()
    {
        var products = ProductCatalogue.Read(Seed);
        var lexicon = ColourLexicon.Read(Attributes);

        var unknown = products
            .Select(product => product.SpanishColour)
            .OfType<string>()
            .Distinct()
            .Where(colour => lexicon.English(colour) is null)
            .ToArray();

        Assert.True(
            unknown.Length == 0,
            $"No English label for: {String.Join(", ", unknown)}. Add the option to seed/attributes.sample.json.");
    }

    [Theory]
    [InlineData("oliva", "olive")]
    [InlineData("azul marino", "navy blue")]
    [InlineData("acero inoxidable", "stainless steel")]
    public void The_lexicon_translates_what_the_products_actually_say(string spanish, string english)
    {
        var lexicon = ColourLexicon.Read(Attributes);

        Assert.Equal(english, lexicon.English(spanish));
    }
}
