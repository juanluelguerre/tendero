using ElGuerre.Tendero.Channels.Domain;
using ElGuerre.Tendero.Tests;
using Xunit;

namespace ElGuerre.Tendero.Channels.Tests;

/// <summary>
/// The held orders' evidence of use (P16-29; the tracking plan's *Sales channels*): why an
/// order was held, and how it left. Found by the Stryker run after P16-29: only the
/// integration tests listened, so nothing a unit suite ran could tell the tags apart.
/// </summary>
public sealed class ChannelMetricsTests
{
    [Theory]
    [InlineData(HeldOrderReason.NotInCatalogue, "not_in_catalogue")]
    [InlineData(HeldOrderReason.NoAddress, "no_address")]
    [InlineData(HeldOrderReason.TotalsDoNotAddUp, "totals_do_not_add_up")]
    public void A_held_order_is_counted_by_its_channel_and_why(HeldOrderReason reason, string tag)
    {
        using var recorder = new InstrumentRecorder("tendero.channel.orders.held");

        ChannelMetrics.OrderHeld("shopify", reason);

        var (value, tags) = Assert.Single(recorder.Measured("tendero.channel.orders.held"));
        Assert.Equal((1, "shopify", tag), (value, (string?)tags["channel"], (string?)tags["reason"]));
    }

    [Theory]
    [InlineData(ChannelMetrics.ReleasedReceived, "received")]
    [InlineData(ChannelMetrics.ReleasedClosed, "closed")]
    public void A_released_order_is_counted_by_its_channel_and_how_it_left(string outcome, string tag)
    {
        using var recorder = new InstrumentRecorder("tendero.channel.orders.released");

        ChannelMetrics.OrderReleased("shopify", outcome);

        var (value, tags) = Assert.Single(recorder.Measured("tendero.channel.orders.released"));
        Assert.Equal((1, "shopify", tag), (value, (string?)tags["channel"], (string?)tags["outcome"]));
    }
}
