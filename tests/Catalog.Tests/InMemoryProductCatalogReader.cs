using ElGuerre.Tendero.Catalog.Domain;
using ElGuerre.Tendero.Catalog.Ports;

namespace ElGuerre.Tendero.Catalog.Tests;

/// <summary>
/// The catalogue's read side over a list, shared by every slice test that needs
/// one. It was nested inside <c>ListProductsTests</c> while it had one caller;
/// the product page made it two, and a fake copied is a fake that drifts.
/// </summary>
internal sealed class InMemoryProductCatalogReader(params Product[] products) : IProductCatalogReader
{
    public Task<ProductPage> ListAsync(
        ProductStatus? status, int page, int pageSize, CancellationToken ct)
    {
        var matching = products.Where(p => status is null || p.Status == status).ToList();

        return Task.FromResult(new ProductPage(
            [.. matching.Skip((page - 1) * pageSize).Take(pageSize)],
            matching.Count));
    }

    /// <summary>
    /// Plain equality, and there is nothing else to reproduce: a code is unique
    /// by constraint, so there is no tie-break, no culture and no fallback for a
    /// fake to get subtly wrong. That the double is this boring is the clearest
    /// evidence the key got simpler (ADR 0026) — the version of this that looked
    /// products up by slug had to mirror an ordering rule to stay honest.
    /// </summary>
    public Task<Product?> FindByCodeAsync(string code, CancellationToken ct) =>
        Task.FromResult(products.FirstOrDefault(product => product.Code == code));
}
