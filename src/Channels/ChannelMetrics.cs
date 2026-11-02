using System.Diagnostics.Metrics;
using ElGuerre.Tendero.Channels.Domain;
using ElGuerre.Tendero.SharedKernel;

namespace ElGuerre.Tendero.Channels;

/// <summary>
/// The channels' evidence of use (ADR 0036; the tracking plan's *Sales channels*):
/// whether a channel keeps up and how often it refuses, whether its orders arrive and
/// by which path, and whether the margin bounds overselling.
/// </summary>
public static class ChannelMetrics
{
    public const string ProductKind = "product";
    public const string StockKind = "stock";
    public const string ShipmentKind = "shipment";

    private static readonly Meter Meter = new(TelemetrySources.Channels);

    private static readonly Counter<long> Publications = Meter.CreateCounter<long>(
        "tendero.channel.publications", unit: "{publication}",
        description: "Every product, stock or shipment publication to a sales channel, by its outcome.");

    public static void Publication(string channel, string kind, ChannelListingOutcome outcome) =>
        Publication(channel, kind, outcome switch
        {
            ChannelListingOutcome.Published => "published",
            ChannelListingOutcome.Refused => "refused",
            ChannelListingOutcome.Unpublished => "unpublished",
        });

    public static void Publication(string channel, string kind, string outcome) =>
        Publications.Add(1, new("channel", channel), new("kind", kind), new("outcome", outcome));
}
