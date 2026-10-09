using System.Globalization;
using System.Text.RegularExpressions;

namespace ElGuerre.Tendero.Assist.Turns;

/// <summary>
/// The figures of a sentence against the text it rests on (the Tino spec,
/// *Grounding*, the second check; and Steward's answer said again in another
/// language, against the answer it was). A model that scores meaning is weak with
/// numbers, so a number is not scored: it is compared. Every figure a reason
/// states must be one the fact states.
/// </summary>
public static partial class Figures
{
    /// <summary>Whether every number in <paramref name="sentence"/> is a number of <paramref name="premise"/>.</summary>
    public static bool AllIn(string sentence, string premise)
    {
        var known = Of(premise).ToHashSet();
        return Of(sentence).All(known.Contains);
    }

    /// <summary>The numbers a text states, as values: <c>0,8</c> and <c>0.80</c> are the same figure, in either language.</summary>
    public static IEnumerable<decimal> Of(string text) =>
        Number().Matches(text).Select(match => decimal.Parse(match.Value.Replace(',', '.'), CultureInfo.InvariantCulture));

    [GeneratedRegex(@"\d+(?:[.,]\d+)?", RegexOptions.CultureInvariant)]
    private static partial Regex Number();
}
