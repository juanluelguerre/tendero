using Microsoft.EntityFrameworkCore;
using Tendero.Catalog.Domain;
using Tendero.Catalog.Features.ImportProducts;
using Tendero.Search.Features.ProjectProductToIndex;
using Tendero.SharedKernel;

namespace Tendero.Persistence;

/// <summary>
/// Adaptador de los dos puertos de Product que hoy existen. Son dos interfaces
/// distintas a propósito: la importación necesita buscar por referencia externa
/// y añadir; la proyección al índice sólo necesita leer por id.
/// </summary>
internal sealed class EfProductRepository(TenderoDbContext context) : IProductRepository, IProductReader
{
    public Task<Product?> FindByExternalReferenceAsync(string source, string externalId, CancellationToken ct) =>
        context.Products.FirstOrDefaultAsync(
            product => product.ExternalReferences.Any(
                reference => reference.Source == source && reference.ExternalId == externalId),
            ct);

    public void Add(Product product) => context.Products.Add(product);

    // AsNoTracking: el worker de indexación lee para proyectar, nunca para mutar.
    public Task<Product?> GetByIdAsync(ProductId id, CancellationToken ct) =>
        context.Products.AsNoTracking().FirstOrDefaultAsync(product => product.Id == id, ct);
}

internal sealed class EfUnitOfWork(TenderoDbContext context) : IUnitOfWork
{
    public Task SaveChangesAsync(CancellationToken ct) => context.SaveChangesAsync(ct);
}
