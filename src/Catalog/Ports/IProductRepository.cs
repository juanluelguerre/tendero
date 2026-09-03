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

    /// <summary>
    /// What a set of SKUs is called.
    ///
    /// It exists because of a boundary rather than in spite of one. `Inventory`
    /// may reference the SharedKernel and nothing else — stock exists without a
    /// catalogue exactly as it exists without orders (ADR 0024) — so
    /// `GET /api/inventory/stock` structurally cannot say what
    /// `B05DEFG606-DEFAULT` is. The catalogue can, and the screen that needs
    /// both asks both.
    ///
    /// A SET and not one at a time: a stock grid is a page of SKUs, and a
    /// lookup per row is the N+1 the whole reader exists to avoid.
    ///
    /// A SKU with no product comes back missing rather than invented. That is
    /// not defensive: the same rule that lets Inventory ignore Catalog lets
    /// stock OUTLIVE a product, so a shelf holding something the catalogue no
    /// longer lists is a real state and the screen should say so.
    /// </summary>
    Task<IReadOnlyList<SkuDescription>> DescribeSkusAsync(
        IReadOnlyCollection<string> skus, string culture, CancellationToken ct);
}

/// <summary>
/// What one SKU is, in the culture it was asked for.
///
/// It carries the variant's raw COORDINATES rather than a rendered label,
/// because rendering one needs the attribute definitions and a repository has
/// no business holding a translation catalogue. The slice renders, exactly as
/// the product page does — same axes, same definitions, one place that knows
/// how an axis becomes words.
/// </summary>
public sealed record SkuDescription(
    string Sku,
    string ProductCode,
    string ProductName,
    string Slug,
    IReadOnlyList<string> AxisOrder,
    IReadOnlyDictionary<string, string> AxisValues);
