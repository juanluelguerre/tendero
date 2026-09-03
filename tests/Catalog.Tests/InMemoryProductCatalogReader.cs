using ElGuerre.Tendero.Catalog.Domain;
using ElGuerre.Tendero.Catalog.Ports;

namespace ElGuerre.Tendero.Catalog.Tests;

/// <summary>
/// The catalogue's read side over a list, shared by every slice test that needs
/// one. It was nested inside <c>ListProductsTests</c> while it had one caller;
/// the product page made it two, and a fake copied is a fake that drifts.
///
/// It reproduces the ADAPTER's slug semantics deliberately — requested culture
/// first, any culture second — because that ordering is the behaviour the page
/// depends on, and a fake that resolved slugs some easier way would let a test
/// pass over an adapter that does the wrong thing.
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

    public Task<Product?> FindBySlugAsync(string slug, string culture, CancellationToken ct)
    {
        var inCulture = products.FirstOrDefault(product =>
            product.Slug.Values.TryGetValue(culture, out var value) && value == slug);

        return Task.FromResult(
            inCulture ??
            products.FirstOrDefault(product => product.Slug.Values.Values.Contains(slug)));
    }
}
