using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace ElGuerre.Tendero.SearchEval.Voice;

/// <summary>
/// How a transcript is compared (docs/analysis/voice-recordings.md): what was said
/// and what an engine heard are normalised alike — lowercased, punctuation removed,
/// numbers said in words written as figures — so the rate counts what a search
/// would see. "doce" and "12" are the same word heard, and so are "¿Qué?" and "qué".
/// </summary>
public static partial class Transcripts
{
    public static string Normalise(string text, string culture)
    {
        var lowered = text.Normalize(NormalizationForm.FormC).ToLower(CultureInfo.InvariantCulture)
            .Replace("'", string.Empty, StringComparison.Ordinal)
            .Replace("’", string.Empty, StringComparison.Ordinal)
            // A currency sign is the word written as a symbol: "200€" was said "doscientos euros".
            .Replace("€", " euros ", StringComparison.Ordinal);
        var words = NotWordOrDigit().Replace(lowered, " ").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        // Numbers first, while "dieciséis" still has the accent the list has; then the accents.
        return string.Join(' ', NumbersAsFigures(words, culture == "en" ? English : Spanish).Select(WithoutAccents));
    }

    /// <summary>
    /// "qué" as "que", "pingüino" as "pinguino": an accent is not a word heard
    /// differently. The ñ is a letter, and stays. A table rather than Unicode
    /// decomposition: the repository builds with InvariantGlobalization, under which
    /// <c>Normalize(FormD)</c> decomposes nothing.
    /// </summary>
    private static string WithoutAccents(string word)
    {
        var folded = new StringBuilder(word.Length);
        foreach (var letter in word)
            folded.Append(Accented.IndexOf(letter) is >= 0 and var at ? Plain[at] : letter);
        return folded.ToString();
    }

    private const string Accented = "áàâäéèêëíìîïóòôöúùûüç";
    private const string Plain = "aaaaeeeeiiiioooouuuuc";

    /// <summary>The word-level edits — changed, dropped, added — between what was said and what was heard, and the words said.</summary>
    public static (int Edits, int Words) Errors(string said, string heard)
    {
        var reference = said.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var hypothesis = heard.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var row = Enumerable.Range(0, hypothesis.Length + 1).ToArray();
        for (var i = 1; i <= reference.Length; i++)
        {
            var next = new int[hypothesis.Length + 1];
            next[0] = i;
            for (var j = 1; j <= hypothesis.Length; j++)
                next[j] = Math.Min(Math.Min(row[j] + 1, next[j - 1] + 1), row[j - 1] + (reference[i - 1] == hypothesis[j - 1] ? 0 : 1));
            row = next;
        }

        return (row[hypothesis.Length], reference.Length);
    }

    private sealed record NumberWords(IReadOnlyDictionary<string, int> Values, IReadOnlySet<string> Hundreds,
        IReadOnlySet<string> Thousands, string Joiner);

    /// <summary>A run of number words is one figure. The articles are not in the list: "un kit" and "a lamp" stay words.</summary>
    private static IEnumerable<string> NumbersAsFigures(string[] words, NumberWords numbers)
    {
        for (var i = 0; i < words.Length;)
        {
            if (!IsNumber(words[i], numbers))
            {
                yield return words[i++];
                continue;
            }

            long total = 0, current = 0;
            while (i < words.Length)
            {
                var word = words[i];
                if (word == numbers.Joiner && i + 1 < words.Length && IsNumber(words[i + 1], numbers) && current > 0)
                {
                    i++;
                    continue;
                }
                if (!IsNumber(word, numbers))
                    break;

                if (long.TryParse(word, out var figure))
                    current += figure;
                else if (numbers.Hundreds.Contains(word))
                    current = Math.Max(current, 1) * 100;
                else if (numbers.Thousands.Contains(word))
                {
                    total += Math.Max(current, 1) * 1000;
                    current = 0;
                }
                else
                    current += numbers.Values[word];
                i++;
            }

            yield return (total + current).ToString(CultureInfo.InvariantCulture);
        }
    }

    private static bool IsNumber(string word, NumberWords numbers) =>
        numbers.Values.ContainsKey(word) || numbers.Hundreds.Contains(word) || numbers.Thousands.Contains(word)
        || (word.Length > 0 && word.All(char.IsAsciiDigit));

    private static readonly NumberWords Spanish = new(
        new Dictionary<string, int>
        {
            ["cero"] = 0,
            ["dos"] = 2,
            ["tres"] = 3,
            ["cuatro"] = 4,
            ["cinco"] = 5,
            ["seis"] = 6,
            ["siete"] = 7,
            ["ocho"] = 8,
            ["nueve"] = 9,
            ["diez"] = 10,
            ["once"] = 11,
            ["doce"] = 12,
            ["trece"] = 13,
            ["catorce"] = 14,
            ["quince"] = 15,
            ["dieciséis"] = 16,
            ["diecisiete"] = 17,
            ["dieciocho"] = 18,
            ["diecinueve"] = 19,
            ["veinte"] = 20,
            ["veintiuno"] = 21,
            ["veintiún"] = 21,
            ["veintidós"] = 22,
            ["veintitrés"] = 23,
            ["veinticuatro"] = 24,
            ["veinticinco"] = 25,
            ["veintiséis"] = 26,
            ["veintisiete"] = 27,
            ["veintiocho"] = 28,
            ["veintinueve"] = 29,
            ["treinta"] = 30,
            ["cuarenta"] = 40,
            ["cincuenta"] = 50,
            ["sesenta"] = 60,
            ["setenta"] = 70,
            ["ochenta"] = 80,
            ["noventa"] = 90,
            ["ciento"] = 100,
            ["cien"] = 100,
            ["doscientos"] = 200,
            ["doscientas"] = 200,
            ["trescientos"] = 300,
            ["trescientas"] = 300,
            ["cuatrocientos"] = 400,
            ["cuatrocientas"] = 400,
            ["quinientos"] = 500,
            ["quinientas"] = 500,
            ["seiscientos"] = 600,
            ["seiscientas"] = 600,
            ["setecientos"] = 700,
            ["setecientas"] = 700,
            ["ochocientos"] = 800,
            ["ochocientas"] = 800,
            ["novecientos"] = 900,
            ["novecientas"] = 900,
        },
        new HashSet<string>(),
        new HashSet<string> { "mil" },
        "y");

    private static readonly NumberWords English = new(
        new Dictionary<string, int>
        {
            ["zero"] = 0,
            ["one"] = 1,
            ["two"] = 2,
            ["three"] = 3,
            ["four"] = 4,
            ["five"] = 5,
            ["six"] = 6,
            ["seven"] = 7,
            ["eight"] = 8,
            ["nine"] = 9,
            ["ten"] = 10,
            ["eleven"] = 11,
            ["twelve"] = 12,
            ["thirteen"] = 13,
            ["fourteen"] = 14,
            ["fifteen"] = 15,
            ["sixteen"] = 16,
            ["seventeen"] = 17,
            ["eighteen"] = 18,
            ["nineteen"] = 19,
            ["twenty"] = 20,
            ["thirty"] = 30,
            ["forty"] = 40,
            ["fifty"] = 50,
            ["sixty"] = 60,
            ["seventy"] = 70,
            ["eighty"] = 80,
            ["ninety"] = 90,
        },
        new HashSet<string> { "hundred" },
        new HashSet<string> { "thousand" },
        "and");

    [GeneratedRegex(@"[^\p{L}\p{Nd}\s]")]
    private static partial Regex NotWordOrDigit();
}
