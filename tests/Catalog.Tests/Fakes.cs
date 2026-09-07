using ElGuerre.Tendero.Catalog.Domain;
using ElGuerre.Tendero.Catalog.Ports;
using ElGuerre.Tendero.SharedKernel;

namespace ElGuerre.Tendero.Catalog.Tests;

/// <summary>
/// Deterministic doubles instead of mocks, which is the repository's rule
/// (docs/testing.md): what matters about a port is what reached it, and a list
/// says that more clearly than a call verification.
///
/// They lived nested and private inside PublishProductTests. They came out here
/// when a second slice needed them, which is exactly the moment something stops
/// being one test's detail and becomes tooling.
/// </summary>
internal sealed class InMemoryProductRepository(params Product[] products) : IProductRepository
{
    private readonly List<Product> products = [.. products];

    public Task<Product?> FindByIdAsync(ProductId id, CancellationToken ct) =>
        Task.FromResult(this.products.SingleOrDefault(p => p.Id == id));

    public Task<Product?> FindByExternalReferenceAsync(string source, string externalId, CancellationToken ct) =>
        Task.FromResult(
            this.products.SingleOrDefault(p =>
                p.ExternalReferences.Any(r => r.Source == source && r.ExternalId == externalId)));

    public void Add(Product product) => this.products.Add(product);
}

/// <summary>
/// Counts the saves. It is what lets us assert that publishing something already
/// active writes NOTHING — an assertion on the final state could not tell "it did
/// nothing" from "it did it twice".
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
