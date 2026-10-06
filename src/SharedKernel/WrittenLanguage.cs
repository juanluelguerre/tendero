using System.Text.RegularExpressions;

namespace ElGuerre.Tendero.SharedKernel;

/// <summary>
/// Which of the shop's two languages a text is written in, told without a model.
/// Crude on purpose — function words, counted — and enough for what it is asked:
/// a claim's statement written in the wrong language (P10-0 saw one "Spanish"
/// statement in six in English at 4 bits), and an agent's answer in a language
/// that is not the screen's. A product's name inside the text is the catalogue's
/// language and not the text's; the function words around it are what is counted.
/// </summary>
public static partial class WrittenLanguage
{
    private static readonly Dictionary<string, HashSet<string>> FunctionWords = new()
    {
        ["es"] = ["el", "la", "los", "las", "es", "de", "con", "y", "se", "que", "un", "una", "para", "en", "tiene", "del", "sin"],
        ["en"] = ["the", "is", "of", "with", "and", "a", "an", "it", "for", "in", "has", "to", "can", "without"],
    };

    /// <summary>Whether <paramref name="text"/> can pass for <paramref name="language"/>: a text that says nothing either way does.</summary>
    public static bool Is(string? text, string language)
    {
        var (spanish, english) = Count(text);
        return language == "es" ? spanish >= english : english >= spanish;
    }

    /// <summary>The language <paramref name="text"/> is plainly written in, or null when its words do not say.</summary>
    public static string? Of(string? text)
    {
        var (spanish, english) = Count(text);
        return spanish == english ? null : spanish > english ? "es" : "en";
    }

    private static (int Spanish, int English) Count(string? text)
    {
        var words = Words().Matches((text ?? "").ToLowerInvariant()).Select(match => match.Value).ToArray();
        return (words.Count(FunctionWords["es"].Contains), words.Count(FunctionWords["en"].Contains));
    }

    [GeneratedRegex("[a-záéíóúñü]+")]
    private static partial Regex Words();
}
