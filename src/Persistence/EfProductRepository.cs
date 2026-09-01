using ElGuerre.Tendero.Catalog.Domain;
using ElGuerre.Tendero.Catalog.Ports;
using ElGuerre.Tendero.Search.Contracts;
using ElGuerre.Tendero.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace ElGuerre.Tendero.Persistence;

/// <summary>
/// Adaptador de los dos puertos de Product que hoy existen. Son dos interfaces
/// distintas a propósito: la importación necesita buscar por referencia externa
/// y añadir; la proyección al índice sólo necesita leer por id.
/// </summary>
internal sealed class EfProductRepository(TenderoDbContext context)
    : IProductRepository, IProductReader, IProductCatalogReader
{
    // Dos consultas y no una con paginacion en memoria: el total es del filtro
    // completo, no de la pagina, porque una cola de revision necesita decir
    // cuantos quedan. Contar en SQL evita traerse el catalogo para descartarlo.
    public async Task<ProductPage> ListAsync(
        ProductStatus? status, int page, int pageSize, CancellationToken ct)
    {
        var query = context.Products.AsNoTracking();

        if (status is not null)
            query = query.Where(product => product.Status == status);

        var total = await query.CountAsync(ct);

        var items = await query
            // Lo mas recientemente tocado primero: en una cola de revision lo
            // que acaba de importarse es lo que espera decision.
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
