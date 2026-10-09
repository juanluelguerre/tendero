using System.Globalization;
using ElGuerre.Tendero.Knowledge.Domain;
using ElGuerre.Tendero.SharedKernel;

namespace ElGuerre.Tendero.Knowledge.Extraction;

/// <summary>Why a proposed claim never reached the review queue — the tracking plan's reasons.</summary>
public enum DropReason
{
    /// <summary>Its Spanish quote is not in the description, even allowing for case.</summary>
    NotInTheText,

    /// <summary>A statement "in Spanish" written in English, or the other way round (invariant 6).</summary>
    WrongLanguage,

    /// <summary>Another claim already rests on the same words.</summary>
    Duplicate,
}

/// <summary>A claim that passed: where it rests, and what it states and types, normalised.</summary>
/// <param name="Confidence">What the extractor said of itself, carried through untouched.</param>
public sealed record VettedClaim(
    Quote QuoteSpanish,
    Quote? QuoteEnglish,
    LocalizedText Statement,
    string? AttributeCode,
    string? Value,
    bool TypingRefused,
    double? Confidence = null);

/// <summary>
/// What code checks before a claim is ever proposed (ADR 0018, 4) — every rule
/// P10-0 measured failing. Pure: the documents and the vocabulary are values.
/// </summary>
public static partial class ClaimVetting
{
    public static (IReadOnlyList<VettedClaim> Kept, IReadOnlyList<DropReason> Dropped) Vet(
        IReadOnlyList<ProposedClaim> proposed,
        SourceDocument spanish,
        SourceDocument? english,
        IReadOnlyList<AttributeSpec> vocabulary)
    {
        ArgumentNullException.ThrowIfNull(proposed);
        ArgumentNullException.ThrowIfNull(spanish);
        ArgumentNullException.ThrowIfNull(vocabulary);

        var kept = new List<VettedClaim>();
        var dropped = new List<DropReason>();
        var attributes = vocabulary.ToDictionary(spec => spec.Code, StringComparer.OrdinalIgnoreCase);

        foreach (var claim in proposed)
        {
            if (spanish.Locate(claim.QuoteSpanish) is not { } quote)
            {
                dropped.Add(DropReason.NotInTheText);
                continue;
            }

            if (kept.Any(other => other.QuoteSpanish.Start == quote.Start && other.QuoteSpanish.End == quote.End
                                  && other.AttributeCode == Normalise(claim, attributes).Code))
            {
                dropped.Add(DropReason.Duplicate);
                continue;
            }

            var statement = StatementOf(claim, attributes, quote);
            if (statement is null)
            {
                dropped.Add(DropReason.WrongLanguage);
                continue;
            }

            var (code, value, refused) = Normalise(claim, attributes);
            kept.Add(new VettedClaim(quote, english?.Locate(claim.QuoteEnglish), statement, code, value, refused, claim.Confidence));
        }

        return (kept, dropped);
    }

    /// <summary>
    /// The attribute and value, or neither. A value that does not parse against
    /// its attribute's kind, unit and options — <c>MATERIAL = WOOD</c> — is not
    /// stored; the claim stays, untyped, and says its typing was refused.
    /// </summary>
    private static (string? Code, string? Value, bool Refused) Normalise(ProposedClaim claim, Dictionary<string, AttributeSpec> attributes)
    {
        if (claim.AttributeCode is null || claim.Value is null)
            return (null, null, false);

        if (!attributes.TryGetValue(claim.AttributeCode, out var spec))
            return (null, null, true);

        var value = claim.Value.Trim();
        string? normalised = spec.Kind switch
        {
            "Number" => double.TryParse(value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
                ? number.ToString(CultureInfo.InvariantCulture) : null,
            "Boolean" => value.ToLowerInvariant() switch
            {
                "true" or "sí" or "si" or "yes" => "true",
                "false" or "no" => "false",
                _ => null,
            },
            "Option" => spec.Options.FirstOrDefault(option => string.Equals(option, value, StringComparison.OrdinalIgnoreCase)),
            _ => value.Length > 0 ? value : null,
        };

        return normalised is null ? (null, null, true) : (spec.Code, normalised, false);
    }

    /// <summary>
    /// The statement in both languages, each in its own. The rules write none: a
    /// typed rule claim is stated from its attribute's labels, and an untyped one
    /// is a measure (<em>"140 g"</em>, <em>"33 x 22 cm"</em>) that reads the same in both.
    /// </summary>
    private static LocalizedText? StatementOf(ProposedClaim claim, Dictionary<string, AttributeSpec> attributes, Quote quote)
    {
        if (claim.StatementSpanish.Length == 0)
        {
            if (claim.AttributeCode is { } code && claim.Value is { } value && attributes.TryGetValue(code, out var spec))
            {
                var unit = spec.Unit is { Length: > 0 } u ? " " + u : "";
                return LocalizedText.From("es", $"{spec.LabelSpanish ?? spec.Label}: {value}{unit}")
                    .With("en", $"{spec.Label}: {value}{unit}");
            }

            return LocalizedText.From("es", quote.Text).With("en", quote.Text);
        }

        return StatementLanguage.Is(claim.StatementSpanish, "es") && StatementLanguage.Is(claim.StatementEnglish, "en")
            ? LocalizedText.From("es", claim.StatementSpanish).With("en", claim.StatementEnglish)
            : null;
    }
}

/// <summary>
/// Whether a statement is written in the language it claims to be. Crude on
/// purpose — function words, counted — and enough to catch what P10-0 saw: one
/// "Spanish" statement in six written in English at 4 bits, every one at 2B.
/// </summary>
public static class StatementLanguage
{
    /// <summary>The kernel's count (<see cref="WrittenLanguage"/>), which an agent's answer is held to as well.</summary>
    public static bool Is(string text, string language) => WrittenLanguage.Is(text, language);
}
