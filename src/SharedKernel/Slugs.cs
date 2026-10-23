using System.Text;

namespace ElGuerre.Tendero.SharedKernel;

/// <summary>
/// A name as lowercase ASCII words joined by hyphens: a product's slug in its
/// URL, and the code a store's location is proposed under when it becomes a
/// warehouse (phase 16). One table, so "Almacén" folds the same way in both.
/// </summary>
public static class Slugs
{
    /// <summary>
    /// Explicit diacritic folding. The natural thing would be
    /// <c>Normalize(NormalizationForm.FormD)</c> followed by dropping the
    /// combining marks — and that is what was written first. It does not work:
    /// the repository builds with <c>InvariantGlobalization=true</c>, and there
    /// Unicode normalisation is a silent NO-OP. It returns the string intact,
    /// with no exception and no warning, so the code looked correct while "café"
    /// kept coming out as <c>café</c> inside the URL.
    ///
    /// A table is uglier and it is the one that works. It covers what an es/en
    /// catalogue can bring; anything not here becomes a separator, which is the
    /// safe failure: an ugly slug, never a character that has to be escaped.
    /// </summary>
    private const string Accented = "áàâäãåéèêëíìîïóòôöõúùûüñçýÿšžœæø";
    private const string Folded = "aaaaaaeeeeiiiiooooouuuuncyyszoao";

    /// <summary>
    /// ASCII letters and digits; everything else separates. Splitting on spaces
    /// alone left brackets and accents inside the URL — "Cafetera Espresso
    /// (12 tazas)" came out as <c>cafetera-espresso-(12-tazas)</c>.
    /// </summary>
    public static string Of(string name)
    {
        var words = new List<string>();
        var word = new StringBuilder(name.Length);

        foreach (var character in name)
        {
            var lowered = char.ToLowerInvariant(character);
            var index = Accented.IndexOf(lowered, StringComparison.Ordinal);
            if (index >= 0)
                lowered = Folded[index];

            if (char.IsAsciiLetterOrDigit(lowered))
            {
                word.Append(lowered);
                continue;
            }

            if (word.Length > 0)
            {
                words.Add(word.ToString());
                word.Clear();
            }
        }

        if (word.Length > 0)
            words.Add(word.ToString());

        return string.Join('-', words);
    }
}
