using ElGuerre.Tendero.Catalog.Domain;
using ElGuerre.Tendero.SharedKernel;
using ElGuerre.Tendero.Tests;
using Xunit;

namespace ElGuerre.Tendero.Catalog.Tests;

/// <summary>
/// An option's swatch is data a storefront paints (R-7.3): a colour as
/// #RRGGBB and nothing else, kept in one case so two spellings never mean two
/// colours.
/// </summary>
public sealed class AttributeDefinitionSwatchTests
{
    private static readonly TestClock Clock = new();

    private static AttributeDefinition Colour() =>
        AttributeDefinition.Define(Clock, "COLOR", LocalizedText.From("es", "color"), AttributeKind.Option, isVariantAxis: true, isFacet: true);

    [Fact]
    public void A_swatch_is_kept_in_upper_case()
    {
        var colour = Colour();

        colour.AddOption(Clock, "navy_blue", LocalizedText.From("es", "azul marino"), "#233a5e");

        Assert.Equal("#233A5E", Assert.Single(colour.Options).Swatch);
    }

    [Fact]
    public void An_option_may_have_no_swatch()
    {
        var colour = Colour();

        colour.AddOption(Clock, "BLACK", LocalizedText.From("es", "negro"));

        Assert.Null(Assert.Single(colour.Options).Swatch);
    }

    [Theory]
    [InlineData("233A5E")]
    [InlineData("#233A5")]
    [InlineData("#GGGGGG")]
    [InlineData("navy")]
    public void Anything_but_a_colour_is_refused(string swatch) =>
        Assert.Throws<ArgumentException>(() => Colour().AddOption(Clock, "X", LocalizedText.From("es", "x"), swatch));
}
