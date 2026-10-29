using ElGuerre.Tendero.Accounts.Ports;
using Microsoft.Extensions.DependencyInjection;

namespace ElGuerre.Tendero.Accounts.Adapters;

/// <summary>
/// The customer sources, keyed by name (ADR 0003), as the catalogue's and the
/// stock's are: the only place in Accounts that talks to the container, and the
/// list comes from it, so a source registered is a source offered.
/// </summary>
internal sealed class KeyedCustomerSourceRegistry(IServiceProvider services) : ICustomerSourceRegistry
{
    public IReadOnlyCollection<string> Sources =>
        [.. services.GetKeyedServices<ICustomerSourceConnector>(KeyedService.AnyKey)
            .Select(connector => connector.Source)
            .Order(StringComparer.Ordinal)];

    public ICustomerSourceConnector Get(string source) =>
        services.GetKeyedService<ICustomerSourceConnector>(source)
        ?? throw new InvalidOperationException($"Unknown customer source '{source}'. Registered sources: {string.Join(", ", Sources)}.");
}
