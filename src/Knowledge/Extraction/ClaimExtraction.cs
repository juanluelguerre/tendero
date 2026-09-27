namespace ElGuerre.Tendero.Knowledge.Extraction;

/// <summary>
/// What a product says about itself, in both languages the shop writes.
/// A value crossing from Catalog, never a reference to it (ADR 0014).
/// </summary>
public sealed record ProductText(string Spanish, string English);

/// <summary>
/// An attribute a claim may be typed with, as Catalog defines it — copied as
/// values, so Knowledge can offer the extractor the vocabulary without knowing
/// what an <c>AttributeDefinition</c> is.
/// </summary>
public sealed record AttributeSpec(
    string Code,
    string Kind,
    string? Unit,
    string Label,
    IReadOnlyList<string> Options,
    string? LabelSpanish = null);

/// <summary>
/// One claim as an extractor proposes it: nothing here is trusted yet. The quote
/// must still be found in the text, the value must still parse against its
/// attribute, and a person must still approve it (invariant 12).
/// </summary>
/// <param name="QuoteSpanish">The span of the Spanish description the claim rests on, as the extractor copied it.</param>
/// <param name="QuoteEnglish">The same, from the English description; empty when the extractor reads Spanish only.</param>
/// <param name="StatementSpanish">The claim as a sentence; empty when the extractor writes none (the rules).</param>
/// <param name="AttributeCode">The attribute it types, when the text states a value for one.</param>
/// <param name="Value">That value, invariant: <c>8</c>, <c>0.45</c>, <c>true</c>, <c>CAST_IRON</c>, <c>20, 24, 28</c>.</param>
/// <param name="Confidence">What the extractor says of itself, when it says anything — not a probability (roadmap B4).</param>
public sealed record ProposedClaim(
    string QuoteSpanish,
    string QuoteEnglish,
    string StatementSpanish,
    string StatementEnglish,
    string? AttributeCode,
    string? Value,
    double? Confidence);

/// <summary>
/// Reads a description and proposes claims. The port the P10-0 spike measures
/// every candidate through, so what was measured is what ships.
/// </summary>
public interface IClaimExtractor
{
    Task<IReadOnlyList<ProposedClaim>> ExtractAsync(
        ProductText text, IReadOnlyList<AttributeSpec> attributes, CancellationToken cancellationToken);
}

/// <summary>The keys the extractors are registered under.</summary>
public static class ClaimExtractors
{
    /// <summary>Patterns over the text: the non-AI path (invariant 8).</summary>
    public const string Rules = "rules";

    /// <summary>A generative model behind an OpenAI-compatible endpoint, its answer held to a JSON Schema.</summary>
    public const string Chat = "chat";
}
