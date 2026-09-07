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

        Assert.Equal(100, products.Count);

        // The English description is what the prompt is built from: it names the
        // object without the invented brand, which is what a generator reads best.
        Assert.All(products, product =>
        {
            Assert.False(string.IsNullOrWhiteSpace(product.ItemId));
            Assert.False(string.IsNullOrWhiteSpace(product.EnglishDescription));
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
        var products = ProductCatalogue.Read(Seed);

        Assert.Equal(65, products.Count(product => product.SpanishColour is not null));
        Assert.Equal(35, products.Count(product => product.SpanishColour is null));
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
            $"No English label for: {string.Join(", ", unknown)}. Add the option to seed/attributes.sample.json.");
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
