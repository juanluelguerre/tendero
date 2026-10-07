using System.Globalization;

namespace ElGuerre.Tendero.Knowledge.Extraction;

/// <summary>
/// What a picture answered, as lines of its reading (phase 13a, I-4).
///
/// **The model reads, code builds the claim** (invariant 12). A picture is asked
/// a closed question — how many — and answers a number. Code writes that number
/// under the reading's prose, one line in each language, and
/// <see cref="PictureAnswersExtractor"/> reads the line back as a typed claim.
/// So the answer is part of the document: it is in the document's identity, a
/// claim quotes it as it quotes any text, and a reviewer reads it beside the
/// picture. One place writes the line and reads it.
///
/// It replaces the extractor over a reading's prose, which the same weights did
/// badly: of seventeen typed claims from twelve pictures, twelve were wrong, and
/// the count — which the prose stated correctly six times of six — was typed
/// twice.
/// </summary>
public static class PictureAnswers
{
    /// <summary>The attribute a count types.</summary>
    public const string Pieces = "PIECES";

    /// <summary>A count of one: what every picture of a single product answers.</summary>
    public const string One = "1";

    private static string Lead(string culture) =>
        culture == "es" ? "Elementos contados en la imagen: " : "Items counted in the picture: ";

    private static string Sure(string culture) => culture == "es" ? " (confianza " : " (confidence ";

    private const string Percent = " %)";

    /// <summary>
    /// The reading's text: the model's prose, and under it what it counted — and
    /// how sure it was, when the model's server said: the probability it gave the
    /// number it wrote, as a whole percentage **rounded down**, so that no answer
    /// reads surer than it was.
    /// </summary>
    public static string Under(string prose, string culture, int count, double? confidence = null)
    {
        ArgumentNullException.ThrowIfNull(prose);
        var sure = confidence is { } probability
            ? $"{Sure(culture)}{((int)Math.Floor(Math.Clamp(probability, 0, 1) * 100)).ToString(CultureInfo.InvariantCulture)}{Percent}"
            : "";
        return $"{prose.TrimEnd()}\n{Lead(culture)}{count.ToString(CultureInfo.InvariantCulture)}{sure}.";
    }

    /// <summary>The count written under a reading, how sure the model was, and the line that says it — or null when there is none.</summary>
    public static (string Line, int Count, double? Confidence)? CountIn(string text, string culture)
    {
        ArgumentNullException.ThrowIfNull(text);
        var lead = Lead(culture);
        var start = text.LastIndexOf("\n" + lead, StringComparison.Ordinal);
        if (start < 0)
            return null;

        var line = text[(start + 1)..];
        if (!line.EndsWith('.'))
            return null;

        var answer = line[lead.Length..^1];
        double? confidence = null;
        if (answer.IndexOf(Sure(culture), StringComparison.Ordinal) is var at and >= 0)
        {
            var percent = answer[(at + Sure(culture).Length)..];
            if (!percent.EndsWith(Percent, StringComparison.Ordinal)
                || !int.TryParse(percent[..^Percent.Length], NumberStyles.None, CultureInfo.InvariantCulture, out var hundredths))
            {
                return null;
            }

            confidence = hundredths / 100.0;
            answer = answer[..at];
        }

        return int.TryParse(answer, NumberStyles.None, CultureInfo.InvariantCulture, out var count) ? (line, count, confidence) : null;
    }
}

/// <summary>
/// Reads the answers written under a picture's reading: no model, and none of
/// the prose. A count of none is nothing seen, and proposes nothing. The claim
/// carries how sure the model was of its answer, when the reading says.
/// </summary>
public sealed class PictureAnswersExtractor : IClaimExtractor
{
    public Task<IReadOnlyList<ProposedClaim>> ExtractAsync(
        ProductText text, IReadOnlyList<AttributeSpec> attributes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(attributes);

        if (!attributes.Any(attribute => string.Equals(attribute.Code, PictureAnswers.Pieces, StringComparison.OrdinalIgnoreCase))
            || PictureAnswers.CountIn(text.Spanish, "es") is not { Count: > 0 } spanish)
        {
            return Task.FromResult<IReadOnlyList<ProposedClaim>>([]);
        }

        var english = PictureAnswers.CountIn(text.English, "en")?.Line ?? "";
        return Task.FromResult<IReadOnlyList<ProposedClaim>>(
            [new ProposedClaim(spanish.Line, english, "", "", PictureAnswers.Pieces, spanish.Count.ToString(CultureInfo.InvariantCulture), spanish.Confidence)]);
    }
}
