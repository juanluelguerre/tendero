using ElGuerre.Tendero.Ordering.Ports;
using Microsoft.Extensions.DependencyInjection;

namespace ElGuerre.Tendero.Ordering.Adapters;

/// <summary>
/// The channels Tendero receives orders from, keyed by name (ADR 0003, ADR 0070): the
/// one place the slice reaches the container, as the import's sources do.
/// </summary>
internal sealed class KeyedChannelOrderSources(IServiceProvider services) : IChannelOrderSources
{
    public IChannelOrderSource Get(string channel) =>
        services.GetKeyedService<IChannelOrderSource>(channel)
        ?? throw new InvalidOperationException($"No sales channel '{channel}' to receive orders from: is its store configured?");
}
