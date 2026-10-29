using ElGuerre.Tendero.Ordering.Ports;
using Microsoft.Extensions.DependencyInjection;

namespace ElGuerre.Tendero.Ordering.Adapters;

/// <summary>
/// The order sources, keyed by name (ADR 0003), as the other imports' are: the only
/// place in Ordering that talks to the container, and the list comes from it, so a
/// source registered is a source offered.
/// </summary>
internal sealed class KeyedOrderSourceRegistry(IServiceProvider services) : IOrderSourceRegistry
{
    public IReadOnlyCollection<string> Sources =>
        [.. services.GetKeyedServices<IOrderSourceConnector>(KeyedService.AnyKey)
            .Select(connector => connector.Source)
            .Order(StringComparer.Ordinal)];

    public IOrderSourceConnector Get(string source) =>
        services.GetKeyedService<IOrderSourceConnector>(source)
        ?? throw new InvalidOperationException($"Unknown order source '{source}'. Registered sources: {string.Join(", ", Sources)}.");
}
