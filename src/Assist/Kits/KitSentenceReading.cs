using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace ElGuerre.Tendero.Assist.Kits;

/// <summary>
/// What code reads in a kit's sentence, with no model (phase 13c, K-6;
/// invariant 12): the budget and the requirements are written in it, so they
/// are read, not guessed. The model is asked only what words cannot settle:
/// which of the shop's kits the sentence means.
/// </summary>
public static partial class KitSentenceReading
{
    /// <summary>
    /// The words that make the number after them a price: "menos de 200", "under
    /// 200". Without one, and without a currency beside it, a number is not a
    /// budget: "para 4 personas" is four people.
    /// </summary>
    private static readonly string[] PriceCues =
    [
        "menos de", "hasta", "maximo", "como maximo", "presupuesto", "presupuesto de", "por debajo de",
        "under", "below", "up to", "less than", "max", "maximum", "budget", "budget of",
    ];

    /// <summary>
    /// The budget a sentence says, in the shop's currency's major units, or none.
    /// A number counts when a currency is written beside it ("200 €", "€59.90",
    /// "300 EUR", "200 euros") or a price cue comes before it ("hasta 120").
    /// Decimals after a comma or a point; a group of three after either is
    /// thousands ("1,250", "1.250").
    /// </summary>
    public static decimal? BudgetIn(string sentence)
    {
        ArgumentNullException.ThrowIfNull(sentence);
        var plain = Plain(sentence);

        foreach (Match found in Amount().Matches(plain))
        {
            var before = plain[..found.Index].TrimEnd();
            var saidAsMoney = found.Groups["before"].Success || found.Groups["after"].Success
                || PriceCues.Any(cue => before.EndsWith(cue, StringComparison.Ordinal)
                                        && (before.Length == cue.Length || !char.IsLetter(before[^(cue.Length + 1)])));
            if (!saidAsMoney)
                continue;

            var amount = Number(found.Groups["number"].Value);
            if (amount > 0)
                return amount;
        }

        return null;
    }

    /// <summary>Whether a requirement's label is in the sentence, as a word: accents and capitals aside.</summary>
    public static bool Mentions(string sentence, string label)
    {
        ArgumentNullException.ThrowIfNull(sentence);
        ArgumentNullException.ThrowIfNull(label);
        var word = Plain(label).Trim();
        return word.Length > 0 && Regex.IsMatch(Plain(sentence), $@"(?<!\w){Regex.Escape(word)}(?!\w)", RegexOptions.CultureInvariant);
    }

    /// <summary>A number as written: the last separator is the decimal one unless three digits follow it.</summary>
    private static decimal Number(string written)
    {
        var last = written.LastIndexOfAny(['.', ',']);
        var decimals = last >= 0 && written.Length - last - 1 != 3 ? written[(last + 1)..] : "";
        var whole = (decimals.Length > 0 ? written[..last] : written).Replace(".", "", StringComparison.Ordinal).Replace(",", "", StringComparison.Ordinal);
        return decimal.Parse(decimals.Length > 0 ? $"{whole}.{decimals}" : whole, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Lower case, accents taken off: what a person types without them is the
    /// same word. A table, not Unicode decomposition: under
    /// <c>InvariantGlobalization</c> <c>Normalize(FormD)</c> leaves "ó" whole,
    /// and "induccion" stopped being "inducción" (seen in this file's test).
    /// </summary>
    private static string Plain(string text)
    {
        var plain = new StringBuilder(text.Length);
        foreach (var character in text.ToLowerInvariant())
            plain.Append(character switch
            {
                'á' or 'à' or 'â' or 'ä' => 'a',
                'é' or 'è' or 'ê' or 'ë' => 'e',
                'í' or 'ì' or 'î' or 'ï' => 'i',
                'ó' or 'ò' or 'ô' or 'ö' => 'o',
                'ú' or 'ù' or 'û' or 'ü' => 'u',
                'ç' => 'c',
                _ => character,
            });
        return plain.ToString();
    }

    [GeneratedRegex(@"(?<before>€\s*)?(?<!\d)(?<number>\d{1,3}(?:[.,]\d{3})+(?:[.,]\d{1,2})?|\d+(?:[.,]\d{1,2})?)(?!\d)(?<after>\s*(?:€|eur(?:os?)?\b))?", RegexOptions.CultureInvariant)]
    private static partial Regex Amount();
}
