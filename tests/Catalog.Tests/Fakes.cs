using ElGuerre.Tendero.Catalog.Domain;
using ElGuerre.Tendero.Catalog.Ports;
using ElGuerre.Tendero.SharedKernel;

namespace ElGuerre.Tendero.Catalog.Tests;

/// <summary>
/// Dobles deterministas en lugar de mocks, que es la regla del repositorio
/// (docs/testing.md): lo que importa de un puerto es qué le llegó, y una lista
/// lo dice más claro que una verificación de llamadas.
///
/// Vivían anidados y privados dentro de PublishProductTests. Salieron aquí al
/// necesitarlos un segundo slice, que es exactamente el momento en que algo deja
/// de ser un detalle de un test y pasa a ser utillaje.
/// </summary>
internal sealed class InMemoryProductRepository(params Product[] products) : IProductRepository
{
    private readonly List<Product> _products = [.. products];

    public Task<Product?> FindByIdAsync(ProductId id, CancellationToken ct) =>
        Task.FromResult(_products.SingleOrDefault(p => p.Id == id));

    public Task<Product?> FindByExternalReferenceAsync(string source, string externalId, CancellationToken ct) =>
        Task.FromResult(_products.SingleOrDefault(
            p => p.ExternalReferences.Any(r => r.Source == source && r.ExternalId == externalId)));

    public void Add(Product product) => _products.Add(product);
}

/// <summary>
/// Cuenta los guardados. Es lo que permite afirmar que publicar algo ya activo
/// NO escribe — una aserción sobre el estado final no distinguiría entre "no
/// hizo nada" y "lo hizo dos veces".
/// </summary>
internal sealed class CountingUnitOfWork : IUnitOfWork
{
    public int SaveCount { get; private set; }

    public Task SaveChangesAsync(CancellationToken ct)
    {
        SaveCount++;
        return Task.CompletedTask;
    }
}
