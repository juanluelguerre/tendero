using Xunit;

namespace ElGuerre.Tendero.SharedKernel.Tests;

/// <summary>
/// An amount inside a sentence written for a reader, in the reader's language
/// (phase 13c). The kit's explanation said "146.35 EUR de 150.00 EUR" to a
/// Spanish reader: a point for the decimals and a code where a Spanish page
/// writes a symbol. Written by hand because the API runs with
/// InvariantGlobalization, where no culture's data is there to format with.
/// </summary>
public sealed class MoneyTextTests
{
    [Theory]
    [InlineData(146.35, "EUR", "es", "146,35 €")]
    [InlineData(146.35, "EUR", "en", "€146.35")]
    [InlineData(5, "EUR", "es", "5,00 €")]
    [InlineData(0.5, "EUR", "en", "€0.50")]
    [InlineData(1250, "EUR", "en", "€1,250.00")]
    [InlineData(1250, "EUR", "es", "1250,00 €")] // Spanish groups from five digits (RAE)
    [InlineData(12500, "EUR", "es", "12.500,00 €")]
    [InlineData(-3.2, "EUR", "es", "-3,20 €")]
    [InlineData(0, "EUR", "es", "0,00 €")] // no sign on nothing (phase 13c's close)
    [InlineData(123456, "EUR", "es", "123.456,00 €")] // a whole part of a multiple of three digits, no leading point
    public void An_amount_is_written_as_its_language_writes_it(decimal amount, string currency, string culture, string written) =>
        Assert.Equal(written, MoneyText.In(new Money(amount, currency), culture));

    /// <summary>A currency with no symbol of its own here keeps its code, and its own number of decimals.</summary>
    [Theory]
    [InlineData(1500, "JPY", "es", "1500 JPY")]
    [InlineData(1500, "JPY", "en", "JPY 1,500")]
    [InlineData(12.5, "USD", "en", "$12.50")]
    [InlineData(12.5, "USD", "es", "12,50 US$")]
    public void Another_currency_keeps_its_own_mark_and_decimals(decimal amount, string currency, string culture, string written) =>
        Assert.Equal(written, MoneyText.In(new Money(amount, currency), culture));

    [Fact]
    public void A_language_the_shop_does_not_speak_is_written_as_the_default()
    {
        Assert.Equal(MoneyText.In(new Money(9.99m, "EUR"), "es"), MoneyText.In(new Money(9.99m, "EUR"), "fr"));
    }
}
