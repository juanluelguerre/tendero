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

/// <summary>
/// What Catalog holds about the products a shopper is looking at, as values
/// (ADR 0014): their names and their attributes, rendered in one culture. The
/// specs table and the comparison (P10-7, P10-8) put Knowledge's quotes beside
/// these; Knowledge never reads a <c>Product</c>. Only products a shopper can
/// see are answered — a draft is not there, as on the product page.
/// </summary>
public interface IProductFacts
{
    Task<IReadOnlyList<ProductFacts>> OfAsync(IReadOnlyList<string> productCodes, string culture, CancellationToken cancellationToken = default);
}

/// <param name="Slug">The product's slug in the culture asked for: a comparison links to each page without a redirect (ADR 0026).</param>
public sealed record ProductFacts(ProductId ProductId, string Code, string Name, string Slug, IReadOnlyList<ProductFact> Attributes);

/// <summary>
/// One attribute as the product page renders it — the wording of a yes/no or a
/// missing value is the interface's, not the API's — and its culture-free
/// <see cref="Key"/>, which an applied claim is matched on.
/// </summary>
public sealed record ProductFact(string AttributeCode, string Label, string Kind, string? Value, decimal? Number, string? Unit, bool? Flag, string Key);
