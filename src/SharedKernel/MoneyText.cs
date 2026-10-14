using System.Globalization;

namespace ElGuerre.Tendero.SharedKernel;

/// <summary>
/// An amount inside a sentence written for a reader, as the reader's language
/// writes money: <c>146,35 €</c> in Spanish, <c>€146.35</c> in English (phase
/// 13c, after the kit's explanation said "146.35 EUR de 150.00 EUR" to a
/// Spanish reader).
///
/// **By hand, on purpose.** The API runs with <c>InvariantGlobalization</c>, so
/// <c>CultureInfo("es")</c> has no data and formats as the invariant culture
/// does. The rules are the two the shop speaks: Spanish uses a decimal comma,
/// groups thousands with a point only from five digits on (RAE: 1250, 12.500),
/// and writes the symbol after the number; English uses a decimal point, groups
/// from four digits with a comma, and writes the symbol first. A currency with
/// no symbol here keeps its code. The decimals are the currency's own
/// (<see cref="MinorUnits"/>): none for the yen.
///
/// Not for the wire (that is <see cref="Money"/> in minor units) and not for a
/// front end (that formats with its own pipe): for a sentence the server
/// writes itself.
/// </summary>
public static class MoneyText
{
    private static readonly Dictionary<string, (string English, string Spanish)> Symbols = new(StringComparer.Ordinal)
    {
        ["EUR"] = ("€", "€"),
        ["USD"] = ("$", "US$"),
        ["GBP"] = ("£", "£"),
    };

    public static string In(Money money, string culture)
    {
        var english = culture == "en";
        var decimals = Decimals(money.Currency);
        var magnitude = Math.Round(Math.Abs(money.Amount), decimals, MidpointRounding.AwayFromZero);
        var digits = magnitude.ToString("F" + decimals.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);

        var point = digits.IndexOf('.');
        var whole = point < 0 ? digits : digits[..point];
        var fraction = point < 0 ? "" : digits[(point + 1)..];

        var grouped = Group(whole, english ? ',' : '.', english ? 4 : 5);
        var number = fraction.Length == 0 ? grouped : grouped + (english ? '.' : ',') + fraction;
        var sign = money.Amount < 0 ? "-" : "";

        if (Symbols.TryGetValue(money.Currency, out var symbol))
            return english ? $"{sign}{symbol.English}{number}" : $"{sign}{number} {symbol.Spanish}";

        return english ? $"{sign}{money.Currency} {number}" : $"{sign}{number} {money.Currency}";
    }

    private static int Decimals(string currency)
    {
        try
        {
            return MinorUnits.Of(currency);
        }
        catch (ArgumentException)
        {
            return 2;
        }
    }

    /// <summary>Thousands grouped, once the whole part has at least <paramref name="from"/> digits.</summary>
    private static string Group(string whole, char separator, int from)
    {
        if (whole.Length < from)
            return whole;

        var groups = new List<string>();
        for (var end = whole.Length; end > 0; end -= 3)
            groups.Insert(0, whole[Math.Max(0, end - 3)..end]);
        return string.Join(separator, groups);
    }
}
