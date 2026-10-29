namespace ElGuerre.Tendero.SharedKernel;

/// <summary>
/// A text as its sentences, so each can be cited on its own (phase 12b): a
/// description is several statements, and an answer that rests on one should name
/// that one and not the paragraph.
///
/// Deliberately plain. A sentence ends at <c>.</c>, <c>!</c> or <c>?</c> followed by
/// a space and then a capital, an opening <c>¿</c> or <c>¡</c>, or the text's end —
/// which keeps <c>2.3 kg</c>, <c>aprox. 2 kg</c> and <c>p. ej. acero</c> whole. A
/// split it gets wrong costs a citation that reads oddly, never a wrong fact: the
/// words are still the description's own.
/// </summary>
public static class Sentences
{
    /// <summary>
    /// A sentence starting with a capital: a note composed from what it is about
    /// and what happened reads "the source counts…" when it is about nothing in
    /// particular, and a sentence does not begin in lowercase (phase 16's close).
    /// </summary>
    public static string StartingUpper(string sentence) =>
        sentence.Length == 0 ? sentence : string.Concat(char.ToUpperInvariant(sentence[0]).ToString(), sentence[1..]);

    public static IReadOnlyList<string> Of(string? text, int most)
    {
        if (string.IsNullOrWhiteSpace(text) || most <= 0)
            return [];

        var sentences = new List<string>();
        var start = 0;
        for (var index = 0; index < text.Length && sentences.Count < most; index++)
        {
            if (text[index] is not ('.' or '!' or '?') || !EndsHere(text, index))
                continue;

            Add(sentences, text[start..(index + 1)]);
            start = index + 1;
        }

        if (sentences.Count < most)
            Add(sentences, text[start..]);
        return sentences;
    }

    private static bool EndsHere(string text, int mark)
    {
        var next = mark + 1;
        if (next >= text.Length)
            return true;
        if (!char.IsWhiteSpace(text[next]))
            return false;

        while (next < text.Length && char.IsWhiteSpace(text[next]))
            next++;
        return next >= text.Length || char.IsUpper(text[next]) || text[next] is '¿' or '¡';
    }

    private static void Add(List<string> sentences, string sentence)
    {
        var trimmed = sentence.Trim();
        if (trimmed.Length > 0)
            sentences.Add(trimmed);
    }
}
