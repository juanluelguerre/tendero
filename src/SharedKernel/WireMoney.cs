using System.Text.Json;
using System.Text.Json.Serialization;

namespace ElGuerre.Tendero.SharedKernel;

/// <summary>
/// How many decimals a currency has (ISO 4217): what turns an amount into the
/// whole number of minor units it travels as. **Not always two** — the yen has
/// none and the Kuwaiti dinar three — and a currency this does not know is
/// refused rather than assumed, because a wrong exponent is an amount off by a
/// factor of ten that looks like any other number.
///
/// The shop sells in one currency (laboratory scale, and
/// <see cref="Money.Decimals"/> still says two for its arithmetic); this is the
/// seam a second one would come in through, and the wire no longer writes the
/// assumption into every amount.
/// </summary>
public static class MinorUnits
{
    private static readonly Dictionary<string, int> Exponents = new(StringComparer.Ordinal)
    {
        ["EUR"] = 2,
        ["USD"] = 2,
        ["GBP"] = 2,
        ["CHF"] = 2,
        ["SEK"] = 2,
        ["NOK"] = 2,
        ["DKK"] = 2,
        ["PLN"] = 2,
        ["CZK"] = 2,
        ["CAD"] = 2,
        ["AUD"] = 2,
        ["MXN"] = 2,
        ["JPY"] = 0,
        ["KRW"] = 0,
        ["KWD"] = 3,
        ["BHD"] = 3,
    };

    /// <exception cref="ArgumentException">The currency is not one whose minor unit is known here.</exception>
    public static int Of(string currency) =>
        Exponents.TryGetValue(currency, out var exponent)
            ? exponent
            : throw new ArgumentException($"'{currency}' is not a currency whose minor unit is known.", nameof(currency));

    /// <summary>How many minor units make one of the currency: a hundred for the euro, one for the yen.</summary>
    public static decimal In(string currency) => Scales[Of(currency)];

    /// <summary>
    /// An amount as a whole number of the currency's minor units, rounded away
    /// from zero as a till would — for whoever is allowed to round (UCP's
    /// mapper, which totals what Ordering already rounded). The wire's own
    /// converter does not call this: it refuses a fraction instead.
    /// </summary>
    public static long From(decimal amount, string currency) =>
        (long)Math.Round(amount * In(currency), MidpointRounding.AwayFromZero);

    /// <summary>Minor units back to the amount they are.</summary>
    public static decimal ToAmount(long minor, string currency) => minor / In(currency);

    /// <summary>An amount to the currency's own precision.</summary>
    public static decimal Round(decimal amount, string currency) =>
        Math.Round(amount, Of(currency), MidpointRounding.AwayFromZero);

    /// <summary>
    /// The largest amount the wire writes or takes, in minor units and either
    /// side of zero: 2⁵³−1, the last whole number a JavaScript reader holds
    /// exactly. Past it a browser reads the nearest double, which is a total
    /// off by a cent that nothing reports. UCP's own bound (`amount.json`).
    /// </summary>
    public const long Largest = 9_007_199_254_740_991;

    private static readonly decimal[] Scales = [1m, 10m, 100m, 1000m];
}

public static partial class WireJson
{
    /// <summary>
    /// Money as every surface says it (phase 13b): <c>{"amount":11390,"currency":"EUR"}</c>
    /// — an integer in the currency's minor unit and the currency beside it.
    /// One converter, for the API, the MCP server and the read tools alike, so a
    /// slice hands over a <see cref="SharedKernel.Money"/> and never spells it.
    /// </summary>
    public static JsonConverter<SharedKernel.Money> Money() => new MinorUnitsMoneyConverter();
}

internal sealed class MinorUnitsMoneyConverter : JsonConverter<Money>
{
    public override Money Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException("Money is an object: an amount in minor units and its currency.");

        long? minor = null;
        string? currency = null;

        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            var name = reader.GetString();
            reader.Read();

            switch (name)
            {
                case "amount" when reader.TokenType == JsonTokenType.Number && reader.TryGetInt64(out var whole):
                    minor = whole;
                    break;
                case "amount":
                    throw new JsonException("Money's amount is a whole number of minor units: 113.90 EUR is 11390.");
                case "currency" when reader.TokenType == JsonTokenType.String:
                    currency = reader.GetString();
                    break;
                default:
                    throw new JsonException($"Money has an amount and a currency; '{name}' is neither.");
            }
        }

        if (minor is null || currency is null)
            throw new JsonException("Money has both an amount and a currency.");
        if (Math.Abs((decimal)minor.Value) > MinorUnits.Largest)
            throw new JsonException($"Money's amount is at most {MinorUnits.Largest} minor units either way.");

        return new Money(minor.Value / Scale(currency), currency);
    }

    public override void Write(Utf8JsonWriter writer, Money value, JsonSerializerOptions options)
    {
        var minor = value.Amount * Scale(value.Currency);

        // Not rounded here. A fraction of a minor unit is whoever produced it
        // forgetting to round where the domain rounds, and a cent made up at
        // the door is a total that does not add up behind it.
        if (minor != decimal.Truncate(minor))
            throw new JsonException($"{value.Amount} {value.Currency} is not a whole number of minor units: round it where it is computed.");
        if (Math.Abs(minor) > MinorUnits.Largest)
            throw new JsonException($"{value.Amount} {value.Currency} is more than the wire carries: {MinorUnits.Largest} minor units either way.");

        writer.WriteStartObject();
        writer.WriteNumber("amount", (long)minor);
        writer.WriteString("currency", value.Currency);
        writer.WriteEndObject();
    }

    private static decimal Scale(string currency)
    {
        try
        {
            return MinorUnits.In(currency);
        }
        catch (ArgumentException unknown)
        {
            throw new JsonException(unknown.Message, unknown);
        }
    }
}
