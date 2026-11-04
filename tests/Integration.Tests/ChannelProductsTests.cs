using ElGuerre.Tendero.Catalog.Ports;
using ElGuerre.Tendero.Channels.Ports;
using ElGuerre.Tendero.SharedKernel;
using ElGuerre.Tendero.Workers.Channels;
using Xunit;

namespace ElGuerre.Tendero.Integration.Tests;

/// <summary>
/// A product as the exports say it, told to a channel (ADR 0070). Its attributes travel
/// in the words a store's filter lists, in the store's language (the fix after the round
/// trip on the lab store): the channel used to send none, and Shopify dropped the ones
/// the store held. No database: the worker's mapping alone.
/// </summary>
public sealed class ChannelProductsTests
{
    private static ExportedProduct Pan(params ExportedAttribute[] attributes) => new(
        "PAN-001", true, "Sartén", "sarten", "Antiadherente.", "Alfar", [], [], attributes,
        [new ExportedVariant("PAN-001-28", null, new Money(29.9m, "EUR"), null, [], [])]);

    [Theory]
    [InlineData("es", "sí")]
    [InlineData("en", "yes")]
    public void Each_attribute_is_told_as_a_filter_lists_it_in_the_stores_language(string culture, string yes)
    {
        var told = ChannelProducts.From(Pan(
            new ExportedAttribute("COLOR", "color", "BLACK", "negro") { Kind = ExportedAttributeKind.Option },
            new ExportedAttribute("CAPACITY_L", "capacidad", "1.5", "1.5 L") { Kind = ExportedAttributeKind.Number },
            new ExportedAttribute("INDUCTION", "inducción", "true", null) { Kind = ExportedAttributeKind.YesNo },
            new ExportedAttribute("DISHWASHER", "lavavajillas", "false", null) { Kind = ExportedAttributeKind.YesNo },
            new ExportedAttribute("MPN", "referencia", "TC-2026", "TC-2026") { Kind = ExportedAttributeKind.Text }), culture);

        Assert.Equal(
            [
                new ChannelAttribute("COLOR", "negro"),
                new ChannelAttribute("CAPACITY_L", "1.5"),
                new ChannelAttribute("INDUCTION", yes),
                new ChannelAttribute("DISHWASHER", "no"),
                new ChannelAttribute("MPN", "TC-2026"),
            ],
            told.Attributes);
    }
}
