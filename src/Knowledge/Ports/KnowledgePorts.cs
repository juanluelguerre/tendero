using ElGuerre.Tendero.Knowledge.Domain;
using ElGuerre.Tendero.Knowledge.Extraction;
using ElGuerre.Tendero.SharedKernel;

namespace ElGuerre.Tendero.Knowledge.Ports;

/// <summary>Knowledge's own tables: documents by their content, claims by product.</summary>
public interface IKnowledgeRepository
{
    Task<SourceDocument?> FindDocumentAsync(SourceDocumentId id, CancellationToken cancellationToken = default);

    /// <summary>The newest document of a product in each culture — what its description says now.</summary>
    Task<IReadOnlyList<SourceDocument>> CurrentDocumentsAsync(ProductId productId, CancellationToken cancellationToken = default);

    /// <summary>The claims that still stand for a product: proposed or approved.</summary>
    Task<IReadOnlyList<ProductClaim>> StandingClaimsAsync(ProductId productId, CancellationToken cancellationToken = default);

    /// <summary>Products whose current Spanish description is waiting for claims, oldest first.</summary>
    Task<IReadOnlyList<ProductId>> WaitingAsync(int limit, CancellationToken cancellationToken = default);

    Task<ProductClaim?> FindClaimAsync(ClaimId id, CancellationToken cancellationToken = default);

    /// <summary>Claims in one status, the weakest-supported first — the order a reviewer should read them in (ADR 0018, 5).</summary>
    Task<IReadOnlyList<ProductClaim>> ClaimsAsync(ClaimStatus status, int limit, CancellationToken cancellationToken = default);

    void Add(SourceDocument document);

    void Add(ProductClaim claim);
}

/// <summary>
/// The attributes a claim may be typed with, as Catalog defines them — handed
/// over as values by whoever composes the two (ADR 0014): Knowledge never reads
/// an <c>AttributeDefinition</c>.
/// </summary>
public interface IAttributeVocabulary
{
    Task<IReadOnlyList<AttributeSpec>> CurrentAsync(CancellationToken cancellationToken = default);
}
