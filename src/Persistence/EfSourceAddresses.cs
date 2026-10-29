using ElGuerre.Tendero.Catalog.Domain;
using ElGuerre.Tendero.Catalog.Ports;
using Microsoft.EntityFrameworkCore;

namespace ElGuerre.Tendero.Persistence;

/// <summary>
/// A source's old addresses (phase 16, P16-11): the collections the import keeps,
/// and the products by the handle kept beside their external ids. One adapter,
/// two ports — the import writes through one, the redirects read through the
/// other — over the same scope's context.
/// </summary>
internal sealed class EfSourceAddresses(TenderoDbContext context, TimeProvider clock) : ISourceCollections, ISourceAddresses
{
    public async Task KeepAsync(string source, string handle, string categoryCode, CancellationToken ct)
    {
        // Find looks at what this scope already tracks before asking Postgres, so
        // a collection kept twice in one import is one row.
        var collection = await context.SourceCollections.FindAsync([source.ToLowerInvariant(), handle.ToLowerInvariant()], ct);
        if (collection is null)
            context.SourceCollections.Add(new SourceCollection(source, handle, categoryCode, clock.GetUtcNow()));
        else
            collection.PointTo(categoryCode, clock.GetUtcNow());
    }

    /// <summary>
    /// The product a source called by this handle. Two sources may have used the
    /// same handle; the one on sale is the one an old address can lead to.
    /// </summary>
    public async Task<ProductAddress?> ProductAsync(string handle, CancellationToken ct)
    {
        var product = await context.Products
            .AsNoTracking()
            .Where(product => product.ExternalReferences.Any(reference => reference.Handle == handle))
            .OrderByDescending(product => product.Status == ProductStatus.Active)
            .FirstOrDefaultAsync(ct);

        return product is null ? null : new ProductAddress(product.Code, product.Slug, product.Status);
    }

    public Task<string?> CategoryAsync(string collectionHandle, CancellationToken ct) =>
        context.SourceCollections
            .AsNoTracking()
            .Where(collection => collection.Handle == collectionHandle)
            .OrderByDescending(collection => collection.UpdatedAt)
            .Select(collection => collection.CategoryCode)
            .FirstOrDefaultAsync(ct);
}
