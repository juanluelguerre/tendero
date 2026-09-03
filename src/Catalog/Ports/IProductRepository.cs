using ElGuerre.Tendero.Catalog.Domain;
using ElGuerre.Tendero.SharedKernel;

namespace ElGuerre.Tendero.Catalog.Ports;

/// <summary>
/// Access to the Product aggregate. It lived inside the ImportProducts slice
/// while that was its only user; with PublishProduct it became shared, and the
/// rule says what is shared goes out to a port, never to a reference between
/// slices (CLAUDE.md, invariant 2). The architecture test checks it.
/// </summary>
public interface IProductRepository
{
    Task<Product?> FindByIdAsync(ProductId id, CancellationToken ct);
    Task<Product?> FindByExternalReferenceAsync(string source, string externalId, CancellationToken ct);
    void Add(Product product);
}

/// <summary>A page of products carrying the total of the whole query, not of the
/// page: a review queue needs to know how many are left.</summary>
public sealed record ProductPage(IReadOnlyList<Product> Items, int Total);

/// <summary>
/// The catalogue's read side. Separate from <see cref="IProductRepository"/>
/// because they are different responsibilities: that one loads aggregates to
/// mutate them, this one projects listings to render them. Mixing them ends in a
/// repository with twenty methods where nobody knows which half each slice uses.
/// </summary>
public interface IProductCatalogReader
{
    Task<ProductPage> ListAsync(ProductStatus? status, int page, int pageSize, CancellationToken ct);

    /// <summary>
    /// The product a URL names. The slug is <see cref="LocalizedText"/> — one per
    /// culture, generated from the name in that culture — so the lookup takes the
    /// culture the caller is asking in.
    ///
    /// It resolves a slug belonging to ANOTHER culture too, and that is not
    /// laxity: the two URLs name the same product, and answering 404 for
    /// <c>/en/p/cafetera-espresso</c> would lose a visitor who is one redirect
    /// away from the page they wanted. The response carries every culture's slug
    /// so the caller can send them to the canonical one — which is the same data
    /// <c>hreflang</c> needs, resolved once here instead of guessed twice there.
    ///
    /// The requested culture wins when a slug is ambiguous across cultures. It is
    /// the only tie-break that keeps a URL meaning one thing.
    /// </summary>
    Task<Product?> FindBySlugAsync(string slug, string culture, CancellationToken ct);
}
