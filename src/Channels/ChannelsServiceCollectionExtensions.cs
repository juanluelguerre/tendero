using ElGuerre.Tendero.Channels.Adapters;
using ElGuerre.Tendero.Channels.Domain;
using ElGuerre.Tendero.Channels.Ports;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace ElGuerre.Tendero.Channels;

/// <summary>What the channels are told, beyond each product's values (ADR 0070).</summary>
public sealed class ChannelOptions
{
    public const string SectionName = "Channels";

    /// <summary>Units held back from every variant's available on every channel; the author kept 2.</summary>
    public int StockMargin { get; set; } = Domain.StockMargin.Default.Units;

    /// <summary>The language a channel is told its products in: the store's primary one.</summary>
    public string Culture { get; set; } = "es";

    /// <summary>How often the orders the channels took are read again: the path they arrive by when webhooks cannot.</summary>
    public TimeSpan ReceiveOrdersEvery { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>How often the whole catalogue and stock are told again, repairing what a missed message left behind.</summary>
    public TimeSpan RepublishEvery { get; set; } = TimeSpan.FromHours(6);
}

public static class ChannelsServiceCollectionExtensions
{
    /// <summary>
    /// The sales channels' context. The channels themselves are each adapter's to
    /// register (<c>AddShopify</c> registers Shopify's when the store is configured):
    /// none registered, nothing is published, and the shop runs as it always has.
    /// </summary>
    public static IServiceCollection AddChannels(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<ChannelOptions>()
            .Bind(configuration.GetSection(ChannelOptions.SectionName))
            .Validate(options => options.StockMargin >= 0, "Channels:StockMargin may not be negative.")
            .Validate(options => options.ReceiveOrdersEvery > TimeSpan.Zero, "Channels:ReceiveOrdersEvery must be positive.")
            .Validate(options => options.RepublishEvery > TimeSpan.Zero, "Channels:RepublishEvery must be positive.")
            .ValidateOnStart();
        services.AddSingleton(provider => new StockMargin(provider.GetRequiredService<IOptions<ChannelOptions>>().Value.StockMargin));
        services.AddScoped<ISalesChannels, KeyedSalesChannels>();
        return services;
    }
}
