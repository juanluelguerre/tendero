using ElGuerre.Tendero.Catalog.Domain;
using ElGuerre.Tendero.Catalog.Ports;

namespace ElGuerre.Tendero.Catalog.Adapters;

/// <summary>A rehearsal's catalogue, in memory for one scope: registered scoped, so it dies with the request.</summary>
internal sealed class RehearsedCatalogue : IRehearsedCatalogue
{
    private readonly List<Product> products = [];
    private readonly Dictionary<string, string> collections = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<Product> Products => this.products;

    public void Bring(Product product) => this.products.Add(product);

    public void Keep(string collectionHandle, string categoryCode) => this.collections[collectionHandle] = categoryCode;

    public string? CategoryOf(string collectionHandle) => this.collections.GetValueOrDefault(collectionHandle);
}
