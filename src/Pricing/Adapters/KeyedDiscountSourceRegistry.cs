using ElGuerre.Tendero.Pricing.Ports;
using Microsoft.Extensions.DependencyInjection;

namespace ElGuerre.Tendero.Pricing.Adapters;

/// <summary>
/// The discount sources, keyed by name (ADR 0003), as the other imports' are: the
/// only place in Pricing that talks to the container, and the list comes from it,
/// so a source registered is a source offered.
/// </summary>
internal sealed class KeyedDiscountSourceRegistry(IServiceProvider services) : IDiscountSourceRegistry
{
    public IReadOnlyCollection<string> Sources =>
        [.. services.GetKeyedServices<IDiscountSourceConnector>(KeyedService.AnyKey)
            .Select(connector => connector.Source)
            .Order(StringComparer.Ordinal)];

    public IDiscountSourceConnector Get(string source) =>
        services.GetKeyedService<IDiscountSourceConnector>(source)
        ?? throw new InvalidOperationException($"Unknown discount source '{source}'. Registered sources: {string.Join(", ", Sources)}.");
}
