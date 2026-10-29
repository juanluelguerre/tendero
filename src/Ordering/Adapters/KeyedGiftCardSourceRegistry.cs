using ElGuerre.Tendero.Ordering.Ports;
using Microsoft.Extensions.DependencyInjection;

namespace ElGuerre.Tendero.Ordering.Adapters;

/// <summary>The gift card sources, keyed by name (ADR 0003), as the order sources are.</summary>
internal sealed class KeyedGiftCardSourceRegistry(IServiceProvider services) : IGiftCardSourceRegistry
{
    public IGiftCardSourceConnector? Find(string source) => services.GetKeyedService<IGiftCardSourceConnector>(source);
}
