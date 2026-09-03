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
    /// The product a URL names, by its public code.
    ///
    /// The code and not the slug, because a URL has to keep meaning one thing.
    /// A slug is derived from a name: two products called the same thing produce
    /// the same one, and renaming a product changes it — so looking up by slug
    /// means guessing between products and losing every inbound link on a
    /// rename. The code is minted once, is unique by constraint, and never moves
    /// (ADR 0026).
    ///
    /// The slug stays in the URL beside it, for humans and for search engines,
    /// and the caller compares the one it was given against the canonical one it
    /// gets back — answering 301 when they differ, which is how a rename stays
    /// an SEO event rather than a broken link.
    /// </summary>
    Task<Product?> FindByCodeAsync(string code, CancellationToken ct);
}
