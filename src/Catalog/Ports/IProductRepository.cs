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
}
