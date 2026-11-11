using System.Text.Json;
using CsCheck;
using Xunit;

namespace ElGuerre.Tendero.SharedKernel.Tests;

/// <summary>
/// Money on the wire (phase 13b, the wire's spec): an integer in the
/// currency's minor unit and the currency beside it, as UCP, ACP and every
/// payment API say it — never a number with a fraction, which a JavaScript
/// client reads as a binary double, and never an amount whose currency is
/// somewhere else in the answer.
/// </summary>
public sealed class WireMoneyTests
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { Converters = { WireJson.Money() } };

    private sealed record Line(string Sku, Money Price, Money? Discount = null);

    [Theory]
    [InlineData("113.90", "EUR", """{"amount":11390,"currency":"EUR"}""")]
    [InlineData("0", "EUR", """{"amount":0,"currency":"EUR"}""")]
    [InlineData("-5", "EUR", """{"amount":-500,"currency":"EUR"}""")]
    [InlineData("1099", "JPY", """{"amount":1099,"currency":"JPY"}""")]
    [InlineData("1.234", "KWD", """{"amount":1234,"currency":"KWD"}""")]
    public void Money_is_written_as_minor_units_and_its_currency(string written, string currency, string json)
    {
        // A decimal cannot be an attribute's argument, and a double is not the same number.
        var amount = decimal.Parse(written, System.Globalization.CultureInfo.InvariantCulture);

        Assert.Equal(json, JsonSerializer.Serialize(new Money(amount, currency), Options));
        Assert.Equal(new Money(amount, currency), JsonSerializer.Deserialize<Money>(json, Options));
    }

    /// <summary>A view that carries money carries it whole, wherever it is — and a missing one is null, not zero.</summary>
    [Fact]
    public void Money_inside_a_view_is_the_same_shape_and_an_absent_one_is_null()
    {
        var json = JsonSerializer.Serialize(new Line("PAN", new Money(49.90m, "EUR")), Options);

        Assert.Equal("""{"sku":"PAN","price":{"amount":4990,"currency":"EUR"},"discount":null}""", json);
        Assert.Equal(new Line("PAN", new Money(49.90m, "EUR")), JsonSerializer.Deserialize<Line>(json, Options));
    }

    /// <summary>
    /// **Write, then read, is the same money** — for every whole number of minor
    /// units, in currencies of no, two and three decimals. The exponent is the
    /// currency's, and it is not always two.
    /// </summary>
    [Fact]
    public void Writing_and_reading_any_whole_number_of_minor_units_gives_the_same_money()
    {
        Gen.Select(Gen.Long[-100_000_000, 100_000_000], Gen.OneOf("EUR", "USD", "GBP", "JPY", "KWD")).Sample((minor, currency) =>
        {
            var money = new Money(minor / (decimal)Math.Pow(10, MinorUnits.Of(currency)), currency);

            var json = JsonSerializer.Serialize(money, Options);

            Assert.Equal($$"""{"amount":{{minor}},"currency":"{{currency}}"}""", json);
            Assert.Equal(money, JsonSerializer.Deserialize<Money>(json, Options));
        }, iter: 2_000);
    }

    /// <summary>
    /// A third of a cent is not money: whoever produced it should have rounded,
    /// once, where the domain rounds. The wire refuses rather than rounds for
    /// them — a cent invented here is a total that does not add up there.
    /// </summary>
    [Theory]
    [InlineData("10.005", "EUR")]
    [InlineData("3.3333", "EUR")]
    [InlineData("10.5", "JPY")]
    public void An_amount_that_is_not_a_whole_number_of_minor_units_is_refused(string written, string currency)
    {
        var amount = decimal.Parse(written, System.Globalization.CultureInfo.InvariantCulture);

        var refused = Assert.Throws<JsonException>(() => JsonSerializer.Serialize(new Money(amount, currency), Options));

        Assert.Contains("minor unit", refused.Message, StringComparison.Ordinal);
    }

    /// <summary>A currency the shop does not know the decimals of is not guessed at two.</summary>
    [Fact]
    public void A_currency_whose_minor_unit_is_not_known_is_refused_and_never_assumed()
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Serialize(new Money(10m, "XXX"), Options));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Money>("""{"amount":1000,"currency":"XXX"}""", Options));
        Assert.Throws<ArgumentException>(() => MinorUnits.Of("XXX"));
    }

    [Theory]
    [InlineData("""{"amount":113.9,"currency":"EUR"}""")]
    [InlineData("""{"amount":"11390","currency":"EUR"}""")]
    [InlineData("""{"amount":11390}""")]
    [InlineData("""{"currency":"EUR"}""")]
    [InlineData("""{"amount":11390,"currency":"eur"}""")]
    [InlineData("""{"amount":11390,"currency":"EURO"}""")]
    [InlineData("11390")]
    [InlineData("""{"amount":11390,"currency":"EUR","cents":true}""")]
    public void What_is_not_minor_units_and_a_currency_code_is_refused(string json)
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Money>(json, Options));
    }

    /// <summary>
    /// **An amount a JavaScript reader can hold.** A JSON number above 2⁵³−1
    /// is read by a browser as the nearest double — a total off by a cent
    /// that nothing reports — so that is the largest amount written or
    /// taken, either way round zero. It is UCP's own bound
    /// (`common/types/amount.json`), and ninety billion euros in cents.
    /// </summary>
    [Fact]
    public void An_amount_beyond_what_a_browser_reads_exactly_is_refused_both_ways()
    {
        const long Largest = 9_007_199_254_740_991;

        Assert.Equal(Largest, MinorUnits.Largest);
        Assert.Equal($$"""{"amount":{{Largest}},"currency":"JPY"}""", JsonSerializer.Serialize(new Money(Largest, "JPY"), Options));
        Assert.Equal(new Money(-Largest, "JPY"), JsonSerializer.Deserialize<Money>($$"""{"amount":-{{Largest}},"currency":"JPY"}""", Options));

        Assert.Throws<JsonException>(() => JsonSerializer.Serialize(new Money(Largest + 1m, "JPY"), Options));
        Assert.Throws<JsonException>(() => JsonSerializer.Serialize(new Money(-Largest - 1m, "JPY"), Options));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Money>($$"""{"amount":{{Largest + 1}},"currency":"JPY"}""", Options));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Money>($$"""{"amount":-{{Largest + 1}},"currency":"JPY"}""", Options));
    }

    [Theory]
    [InlineData("EUR", 2)]
    [InlineData("USD", 2)]
    [InlineData("JPY", 0)]
    [InlineData("KWD", 3)]
    public void A_currency_has_the_minor_unit_iso_4217_gives_it(string currency, int exponent)
    {
        Assert.Equal(exponent, MinorUnits.Of(currency));
    }
}
