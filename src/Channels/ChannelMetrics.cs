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

    /// <summary>A held order received at last.</summary>
    public const string ReleasedReceived = "received";

    /// <summary>A held order cancelled, fulfilled or gone on the channel: nothing left to ship.</summary>
    public const string ReleasedClosed = "closed";

    private static readonly Counter<long> Held = Meter.CreateCounter<long>(
        "tendero.channel.orders.held", unit: "{order}",
        description: "Every order a sales channel took that Tendero could not receive and holds to read again, by why (P16-29).");

    private static readonly Counter<long> Released = Meter.CreateCounter<long>(
        "tendero.channel.orders.released", unit: "{order}",
        description: "Every held order that left the list: received at last, or closed on the channel (P16-29).");

    public static void OrderHeld(string channel, HeldOrderReason reason) =>
        Held.Add(1, new("channel", channel), new("reason", reason switch
        {
            HeldOrderReason.NotInCatalogue => "not_in_catalogue",
            HeldOrderReason.NoAddress => "no_address",
            HeldOrderReason.TotalsDoNotAddUp => "totals_do_not_add_up",
        }));

    public static void OrderReleased(string channel, string outcome) =>
        Released.Add(1, new("channel", channel), new("outcome", outcome));
}
