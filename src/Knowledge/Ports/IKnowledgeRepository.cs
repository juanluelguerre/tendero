using ElGuerre.Tendero.Knowledge.Domain;
using ElGuerre.Tendero.SharedKernel;

namespace ElGuerre.Tendero.Knowledge.Ports;

/// <summary>Knowledge's own tables: documents by their content, claims by product.</summary>
public interface IKnowledgeRepository
{
    Task<SourceDocument?> FindDocumentAsync(SourceDocumentId id, CancellationToken cancellationToken = default);

    /// <summary>
    /// The newest description of a product in each culture — what its description
    /// says now. **Descriptions only**: what a model read in one of its pictures
    /// is the product's document too, and is never its description.
    /// </summary>
    Task<IReadOnlyList<SourceDocument>> CurrentDocumentsAsync(ProductId productId, CancellationToken cancellationToken = default);

    /// <summary>The newest reading of one picture of a product in each culture — what a model last wrote about it.</summary>
    Task<IReadOnlyList<SourceDocument>> CurrentReadingsAsync(ProductId productId, ImageId picture, CancellationToken cancellationToken = default);

    /// <summary>The same for every picture of the product that was ever read: the newest reading of each, in each culture.</summary>
    Task<IReadOnlyList<SourceDocument>> CurrentReadingsAsync(ProductId productId, CancellationToken cancellationToken = default);

    /// <summary>The claims that still stand for a product: proposed or approved.</summary>
    Task<IReadOnlyList<ProductClaim>> StandingClaimsAsync(ProductId productId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Products with a source waiting for claims, oldest first: their current
    /// Spanish description, or the current Spanish reading of one of their pictures.
    /// </summary>
    Task<IReadOnlyList<ProductId>> WaitingAsync(int limit, CancellationToken cancellationToken = default);

    Task<ProductClaim?> FindClaimAsync(ClaimId id, CancellationToken cancellationToken = default);

    /// <summary>
    /// A page of one status's claims, the weakest-supported first — the order a
    /// reviewer should read them in (ADR 0018, 5) — with the claim's id as the
    /// tie-break, so two pages never repeat or skip one. Narrowed by support, and
    /// counted by support before it narrows (R-7.2).
    /// </summary>
    /// <param name="product">One product's claims; every product's when null.</param>
    Task<ClaimPage> ClaimsAsync(
        ClaimStatus status, ClaimSupportFilter support, int page, int pageSize, ProductId? product = null,
        CancellationToken cancellationToken = default);

    void Add(SourceDocument document);

    void Add(ProductClaim claim);
}

/// <summary>Which claims the review queue shows: those under <see cref="ProductClaim.WeakSupport"/>, or every one.</summary>
public enum ClaimSupportFilter
{
    Weak,
    All,
}

/// <param name="Total">How many the filter matched, not how many are on the page.</param>
/// <param name="CountBySupport">Every filter's number over the status's claims,
/// whichever filter was asked for.</param>
public sealed record ClaimPage(
    IReadOnlyList<ProductClaim> Claims, int Total, IReadOnlyDictionary<ClaimSupportFilter, int> CountBySupport);
