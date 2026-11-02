using ElGuerre.Tendero.Channels.Domain;
using ElGuerre.Tendero.Channels.Ports;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace ElGuerre.Tendero.Channels.Tests;

/// <summary>
/// What the host composes with <c>AddChannels</c> (phase 16, S3; ADR 0070): the margin the
/// configuration says — two by default, never below zero — and the channels each adapter
/// registered, found by their key. Found by S3's Stryker close: only the integration
/// tests composed it.
/// </summary>
public sealed class ChannelsCompositionTests
{
    private static ServiceProvider Compose(Dictionary<string, string?> settings, params ISalesChannel[] channels)
    {
        var services = new ServiceCollection();
        services.AddChannels(new ConfigurationBuilder().AddInMemoryCollection(settings).Build());
        foreach (var channel in channels)
            services.AddKeyedSingleton(channel.Channel, channel);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void The_margin_is_two_and_the_culture_spanish_unless_the_configuration_says_otherwise()
    {
        using var unsaid = Compose([]);
        using var said = Compose(new() { ["Channels:StockMargin"] = "3" });
        using var none = Compose(new() { ["Channels:StockMargin"] = "0" });

        Assert.Equal(2, unsaid.GetRequiredService<StockMargin>().Units);
        Assert.Equal("es", unsaid.GetRequiredService<IOptions<ChannelOptions>>().Value.Culture);
        Assert.Equal(3, said.GetRequiredService<StockMargin>().Units);
        Assert.Equal(0, none.GetRequiredService<StockMargin>().Units);
    }

    [Theory]
    [InlineData("Channels:StockMargin", "-1")]
    [InlineData("Channels:ReceiveOrdersEvery", "00:00:00")]
    [InlineData("Channels:RepublishEvery", "00:00:00")]
    public void An_impossible_setting_is_refused(string key, string value)
    {
        using var provider = Compose(new() { [key] = value });

        Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<ChannelOptions>>().Value);
    }

    [Fact]
    public void The_channels_registered_are_the_ones_found_by_their_key()
    {
        var channel = new InMemorySalesChannel();
        using var provider = Compose([], new NamedChannel("zeta"), channel, new NamedChannel("alpha"));
        using var scope = provider.CreateScope();
        var channels = scope.ServiceProvider.GetRequiredService<ISalesChannels>();

        Assert.Equal(["alpha", "memory", "zeta"], channels.Channels);
        Assert.Same(channel, channels.Get("memory"));
        Assert.Throws<InvalidOperationException>(() => channels.Get("shopify"));
    }

    [Fact]
    public void With_no_channel_registered_there_is_none_to_tell()
    {
        using var provider = Compose([]);
        using var scope = provider.CreateScope();

        Assert.Empty(scope.ServiceProvider.GetRequiredService<ISalesChannels>().Channels);
    }

    private sealed class NamedChannel(string channel) : ISalesChannel
    {
        public string Channel => channel;

        public Task<ChannelListingIds> PublishAsync(ChannelProduct product, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<StockPublication> SetAvailableAsync(ChannelStock stock, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task MarkShippedAsync(ChannelShipment shipment, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
