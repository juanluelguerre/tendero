using ElGuerre.Tendero.Channels.Domain;
using Xunit;

namespace ElGuerre.Tendero.Channels.Tests.Domain;

/// <summary>
/// An order a channel took and charged for that Tendero could not receive (P16-29): held,
/// so it is read again and shown, never lost behind the reconciliation's mark.
/// </summary>
public sealed class HeldChannelOrderTests
{
    private static readonly DateTimeOffset At = new(2026, 11, 4, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_held_order_remembers_why_since_when_and_where_the_channel_shows_it()
    {
        var held = HeldChannelOrder.Hold("shopify", "7001", "#1013", HeldOrderReason.NotInCatalogue, "B13BAG0711-OLIVE",
            "https://tendero-dev.myshopify.com/admin/orders/7001", At);

        Assert.Equal(
            ("shopify", "7001", "#1013", HeldOrderReason.NotInCatalogue, "B13BAG0711-OLIVE", "https://tendero-dev.myshopify.com/admin/orders/7001", At, At),
            (held.Channel, held.ExternalId, held.Number, held.Reason, held.About, held.Page, held.HeldSince, held.LastTriedAt));
    }

    /// <summary>Tried again and still not received: why may have changed, since when has not.</summary>
    [Fact]
    public void Tried_again_it_keeps_since_when_and_takes_the_new_reason()
    {
        var held = HeldChannelOrder.Hold("shopify", "7001", "#1013", HeldOrderReason.NotInCatalogue, "SKU-1", null, At);

        held.StillHeld(HeldOrderReason.NoAddress, null, At.AddMinutes(5));

        Assert.Equal((HeldOrderReason.NoAddress, (string?)null, At, At.AddMinutes(5)), (held.Reason, held.About, held.HeldSince, held.LastTriedAt));
    }

    [Theory]
    [InlineData("", "7001", "#1")]
    [InlineData("shopify", " ", "#1")]
    [InlineData("shopify", "7001", "")]
    public void An_order_held_without_its_channel_id_or_number_is_refused(string channel, string id, string number) =>
        Assert.ThrowsAny<ArgumentException>(() => HeldChannelOrder.Hold(channel, id, number, HeldOrderReason.NoAddress, null, null, At));
}
