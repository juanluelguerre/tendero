using Microsoft.EntityFrameworkCore;
using Tendero.Catalog.Domain;
using Tendero.Catalog.Ports;
using Tendero.Search.Contracts;
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

    // CON seguimiento, al contrario que GetByIdAsync: quien busca por id desde un
    // slice lo hace para mutar (publicar, archivar) y confirmar con IUnitOfWork.
    // Con AsNoTracking el cambio de estado se perdería en silencio en SaveChanges.
    public Task<Product?> FindByIdAsync(ProductId id, CancellationToken ct) =>
        context.Products.FirstOrDefaultAsync(product => product.Id == id, ct);

    // AsNoTracking: el worker de indexación lee para proyectar, nunca para mutar.
    public Task<Product?> GetByIdAsync(ProductId id, CancellationToken ct) =>
        context.Products.AsNoTracking().FirstOrDefaultAsync(product => product.Id == id, ct);

    // Igual que arriba, y ademas sin seguimiento por una razon de memoria: el
    // change tracker retendria los 147k productos del catalogo completo durante
    // todo el reindexado, que es justo lo que AsAsyncEnumerable evita.
    public IAsyncEnumerable<Product> StreamAllAsync(CancellationToken ct) =>
        context.Products.AsNoTracking().OrderBy(product => product.CreatedAt).AsAsyncEnumerable();
}
