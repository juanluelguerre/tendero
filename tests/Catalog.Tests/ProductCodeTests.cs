using ElGuerre.Tendero.Catalog.Domain;
using ElGuerre.Tendero.SharedKernel;
using ElGuerre.Tendero.Tests;
using Xunit;

namespace ElGuerre.Tendero.Catalog.Tests;

/// <summary>
/// The public identifier a product URL carries (ADR 0026).
///
/// Most of what is asserted here is about what the code REFUSES to contain, and
/// that is the interesting half: an identifier people read aloud, copy off a
/// screen and type from a printed page is a usability surface, not just a
/// number.
/// </summary>
public sealed class ProductCodeTests
{
    private static readonly TestClock Clock = new();

    /// <summary>
    /// I, L, O and U never appear. The first three because they are what people
    /// get wrong against 1 and 0; the last because thinning the vowels keeps a
    /// random string from spelling something the shop would rather not print on
    /// an invoice.
    ///
    /// Ten thousand draws rather than one: a single sample proves nothing about
    /// an alphabet, and a mistake here would be a character appearing in roughly
    /// one code in eight.
    /// </summary>
    [Fact]
    public void A_code_never_contains_a_character_people_misread()
    {
        for (var i = 0; i < 10_000; i++)
        {
            var code = ProductCode.New();

            Assert.DoesNotContain('I', code);
            Assert.DoesNotContain('L', code);
            Assert.DoesNotContain('O', code);
            Assert.DoesNotContain('U', code);
        }
    }

    [Fact]
    public void A_code_is_ten_characters_and_well_formed()
    {
        var code = ProductCode.New();

        Assert.Equal(ProductCode.Length, code.Length);
        Assert.True(ProductCode.IsWellFormed(code));
    }

    /// <summary>
    /// A weak generator is a smaller keyspace than the one you think you have.
    /// Not a randomness test — it cannot be — but it does catch the failure that
    /// actually happens: a constant, a truncated seed, or an alphabet index that
    /// only ever reaches its first few entries.
    /// </summary>
    [Fact]
    public void Codes_do_not_repeat_and_use_the_whole_alphabet()
    {
        var codes = Enumerable.Range(0, 10_000).Select(_ => ProductCode.New()).ToArray();

        Assert.Equal(codes.Length, codes.Distinct().Count());
        Assert.Equal(32, codes.SelectMany(code => code).Distinct().Count());
    }

    /// <summary>
    /// People lowercase URLs — by hand, in a CMS, in a mail client that "tidies"
    /// links. Answering 404 to a correct code in the wrong case loses a visitor
    /// for nothing.
    /// </summary>
    [Theory]
    [InlineData("k7m2qx9p4t", "K7M2QX9P4T")]
    [InlineData("  K7M2QX9P4T  ", "K7M2QX9P4T")]
    public void A_code_is_forgiving_about_how_it_was_typed(string typed, string expected) =>
        Assert.Equal(expected, ProductCode.Normalise(typed));

    /// <summary>
    /// Crockford's substitutions: somebody who reads a 0 as an O, or a 1 as an
    /// I or an l, still lands on the product. It never GENERATES these; it only
    /// forgives them, which is why the alphabet test above and this one are both
    /// needed and neither implies the other.
    /// </summary>
    [Theory]
    [InlineData("K7MOQX9P4T", "K7M0QX9P4T")]
    [InlineData("KIM2QX9P4T", "K1M2QX9P4T")]
    [InlineData("KLM2QX9P4T", "K1M2QX9P4T")]
    public void A_code_forgives_the_letters_it_refuses_to_use(string misread, string expected) =>
        Assert.Equal(expected, ProductCode.Normalise(misread));

    [Theory]
    [InlineData("")]
    [InlineData("SHORT")]
    [InlineData("K7M2QX9P4TTOOLONG")]
    [InlineData("K7M2QX9P4-")]
    public void Anything_that_is_not_a_code_is_rejected_before_it_reaches_the_database(string candidate) =>
        Assert.False(ProductCode.IsWellFormed(candidate));

    /// <summary>
    /// The promise the URL rests on. A product renamed in both languages keeps
    /// the address it was given, and only the decorative half of the URL moves.
    /// </summary>
    [Fact]
    public void A_products_code_survives_being_renamed()
    {
        var product = Product.Create(
            Clock, LocalizedText.From("es", "Cafetera"), new Money(29.90m, "EUR"));

        var code = product.Code;

        product.UpdateDetails(
            Clock, LocalizedText.From("es", "Cafetera de goteo"),
            description: null, brand: null, category: null);

        Assert.Equal(code, product.Code);
        Assert.Equal("cafetera-de-goteo", product.Slug.In("es"));
    }

    /// <summary>Two products are two addresses, however alike they are
    /// named — which is precisely what a name-derived slug could not promise.</summary>
    [Fact]
    public void Two_products_with_the_same_name_get_different_codes()
    {
        var one = Product.Create(Clock, LocalizedText.From("es", "Camisa"), new Money(1m, "EUR"));
        var two = Product.Create(Clock, LocalizedText.From("es", "Camisa"), new Money(1m, "EUR"));

        Assert.NotEqual(one.Code, two.Code);
        Assert.Equal(one.Slug.In("es"), two.Slug.In("es"));
    }
}
