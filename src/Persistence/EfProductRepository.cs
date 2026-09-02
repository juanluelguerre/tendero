using ElGuerre.Tendero.Catalog.Domain;
using ElGuerre.Tendero.Catalog.Ports;
using ElGuerre.Tendero.Search.Contracts;
using ElGuerre.Tendero.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace ElGuerre.Tendero.Persistence;

/// <summary>
/// The adapter for the two Product ports that exist today. Two distinct
/// interfaces on purpose: importing needs to look up by external reference and
/// add; the index projection only needs to read by id.
/// </summary>
internal sealed class EfProductRepository(TenderoDbContext context)
    : IProductRepository, IProductReader, IProductCatalogReader
{
    // Two queries rather than one with in-memory paging: the total belongs to the
    // whole filter and not to the page, because a review queue needs to say how
    // many are left. Counting in SQL avoids pulling the catalogue in to discard it.
    public async Task<ProductPage> ListAsync(
        ProductStatus? status, int page, int pageSize, CancellationToken ct)
    {
        var query = context.Products.AsNoTracking();

        if (status is not null)
            query = query.Where(product => product.Status == status);

        var total = await query.CountAsync(ct);

        var items = await query
            // Most recently touched first: in a review queue, what has just been
            // imported is what is waiting for a decision.
            .OrderByDescending(product => product.UpdatedAt)
            .ThenBy(product => product.Id)   // desempate estable, o dos paginas pueden repetir fila
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return new ProductPage(items, total);
    }

    public Task<Product?> FindByExternalReferenceAsync(string source, string externalId, CancellationToken ct) =>
        context.Products.FirstOrDefaultAsync(
            product => product.ExternalReferences.Any(
                reference => reference.Source == source && reference.ExternalId == externalId),
            ct);

    public void Add(Product product) => context.Products.Add(product);

    // WITH tracking, unlike GetByIdAsync: whoever looks up by id from a slice is
    // doing it to mutate (publish, archive) and commit through IUnitOfWork. With
    // AsNoTracking the status change would be silently lost in SaveChanges.
    public Task<Product?> FindByIdAsync(ProductId id, CancellationToken ct) =>
        context.Products.FirstOrDefaultAsync(product => product.Id == id, ct);

    // AsNoTracking: the indexing worker reads to project, never to mutate.
    public Task<Product?> GetByIdAsync(ProductId id, CancellationToken ct) =>
        context.Products.AsNoTracking().FirstOrDefaultAsync(product => product.Id == id, ct);

    // Same as above, and additionally untracked for a memory reason: the change
    // tracker would hold the full catalogue's 147k products for the whole
    // reindex, which is exactly what AsAsyncEnumerable avoids.
    public IAsyncEnumerable<Product> StreamAllAsync(CancellationToken ct) =>
        context.Products.AsNoTracking().OrderBy(product => product.CreatedAt).AsAsyncEnumerable();
}
