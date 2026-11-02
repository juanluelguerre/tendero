using ElGuerre.Tendero.Channels.Ports;
using Microsoft.Extensions.DependencyInjection;

namespace ElGuerre.Tendero.Channels.Adapters;

/// <summary>
/// The sales channels, keyed by name (ADR 0003), as the inbound sources are: the only
/// place in Channels that talks to the container, and the list comes from it, so a
/// channel registered — Shopify, when it is configured — is a channel published to.
/// </summary>
internal sealed class KeyedSalesChannels(IServiceProvider services) : ISalesChannels
{
    public IReadOnlyCollection<string> Channels =>
        [.. services.GetKeyedServices<ISalesChannel>(KeyedService.AnyKey)
            .Select(channel => channel.Channel)
            .Order(StringComparer.Ordinal)];

    public ISalesChannel Get(string channel) =>
        services.GetKeyedService<ISalesChannel>(channel)
        ?? throw new InvalidOperationException($"Unknown sales channel '{channel}'. Registered channels: {string.Join(", ", Channels)}.");
}
