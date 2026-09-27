using System.Security.Cryptography;
using System.Text;
using ElGuerre.Tendero.SharedKernel;

namespace ElGuerre.Tendero.Knowledge.Domain;

/// <summary>
/// A document's identity: the SHA-256 of what it is — the product, the culture
/// and the text — as <see cref="ImageId"/> is of an image's bytes. A claim can
/// never point at text that changed under it: changed text is a different
/// document, and the claims resting on the old one are superseded rather than
/// edited (ADR 0018). The product and culture are in the hash because two
/// products may well share a sentence, and must not share a document.
/// </summary>
public readonly record struct SourceDocumentId(string Value)
{
    public static SourceDocumentId Of(ProductId productId, string culture, string text)
    {
        ArgumentNullException.ThrowIfNull(culture);
        ArgumentNullException.ThrowIfNull(text);
        return new(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', productId, culture, text)))));
    }

    public override string ToString() => Value;
}

/// <summary>Where a document's text came from. One kind today; manuals and datasheets are the next ones.</summary>
public enum SourceKind
{
    /// <summary>A product's own description, in one culture, as Catalog held it.</summary>
    ProductDescription,
}

/// <summary>
/// The text a claim rests on, kept as it was read. Immutable: its identity is its
/// content, so there is nothing to update — a new text is a new document.
/// </summary>
public sealed class SourceDocument
{
    public SourceDocumentId Id { get; private set; }
    public SourceKind Kind { get; private set; }

    /// <summary>The product the text describes, as a value (ADR 0014).</summary>
    public ProductId ProductId { get; private set; }

    /// <summary>The text's culture: a description is read per language, each its own document.</summary>
    public string Culture { get; private set; } = "";

    public string Text { get; private set; } = "";
    public DateTimeOffset RetrievedAt { get; private set; }

    /// <summary>
    /// When claims were proposed from it. Not part of its content — its identity
    /// is the text — but the queue's own mark: a document with none is waiting.
    /// </summary>
    public DateTimeOffset? ProposedAt { get; private set; }

    /// <summary>Proposal runs that failed on it. After <see cref="MaxAttempts"/> it stops being retried and says so.</summary>
    public int FailedAttempts { get; private set; }

    public const int MaxAttempts = 3;

    public bool IsWaiting => ProposedAt is null && FailedAttempts < MaxAttempts;

    public void MarkProposed(TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        ProposedAt = clock.GetUtcNow();
    }

    public void RecordFailedAttempt() => FailedAttempts++;

    /// <summary>
    /// The description went back to this text (A → B → A): it is the current
    /// document again, and waits for claims again — the ones it had were
    /// superseded when it was replaced.
    /// </summary>
    public void Reinstate(TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        RetrievedAt = clock.GetUtcNow();
        ProposedAt = null;
        FailedAttempts = 0;
    }

    private SourceDocument() { } // EF Core

    public static SourceDocument Describe(ProductId productId, string culture, string text, TimeProvider clock)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(culture);
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        ArgumentNullException.ThrowIfNull(clock);

        return new SourceDocument
        {
            Id = SourceDocumentId.Of(productId, culture, text),
            Kind = SourceKind.ProductDescription,
            ProductId = productId,
            Culture = culture,
            Text = text,
            RetrievedAt = clock.GetUtcNow(),
        };
    }

    /// <summary>
    /// Where a quote sits in the text, found without regard to case and returned
    /// with the text's own spelling — a model that copied <em>"Cabe en su propio
    /// bolsillo"</em> from <em>"cabe en su propio bolsillo"</em> quoted the text, not
    /// something else (P10-0). Null when the text does not contain it at all.
    /// </summary>
    public Quote? Locate(string quote)
    {
        if (string.IsNullOrWhiteSpace(quote))
            return null;

        var start = Text.IndexOf(quote.Trim(), StringComparison.OrdinalIgnoreCase);
        return start < 0 ? null : new Quote(Text.Substring(start, quote.Trim().Length), start, start + quote.Trim().Length);
    }
}

/// <summary>A span of a document, in the document's own spelling.</summary>
public sealed record Quote(string Text, int Start, int End);
