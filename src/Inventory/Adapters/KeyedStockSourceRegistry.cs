using ElGuerre.Tendero.Inventory.Ports;
using Microsoft.Extensions.DependencyInjection;

namespace ElGuerre.Tendero.Inventory.Adapters;

/// <summary>
/// The stock sources, keyed by name (ADR 0003), as the catalogue's are: the only
/// place in Inventory that talks to the container, and the list comes from it, so
/// a source registered is a source offered.
/// </summary>
internal sealed class KeyedStockSourceRegistry(IServiceProvider services) : IStockSourceRegistry
{
    public IReadOnlyCollection<string> Sources =>
        [.. services.GetKeyedServices<IStockSourceConnector>(KeyedService.AnyKey)
            .Select(connector => connector.Source)
            .Order(StringComparer.Ordinal)];

    public IStockSourceConnector Get(string source) =>
        services.GetKeyedService<IStockSourceConnector>(source)
        ?? throw new InvalidOperationException($"Unknown stock source '{source}'. Registered sources: {string.Join(", ", Sources)}.");
}
