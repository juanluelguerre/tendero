using ElGuerre.Tendero.SharedKernel;

namespace ElGuerre.Tendero.Channels.Domain;

/// <summary>
/// Why an order a channel took is held: each a reason that can be mended, so the order is
/// read again until it is received (P16-29). An order cancelled on the channel before
/// Tendero saw it is not held — there is nothing to ship — and neither is a channel no
/// order can come from, which is configuration, not an order.
/// </summary>
public enum HeldOrderReason
{
    /// <summary>A line sells something the catalogue does not: mended by publishing or importing it.</summary>
    NotInCatalogue,

    /// <summary>Nowhere to send it: mended by correcting the order on the channel.</summary>
    NoAddress,

    /// <summary>The channel's figures do not make an order by Tendero's arithmetic: a bug of ours, for someone to see.</summary>
    TotalsDoNotAddUp,
}

/// <summary>
/// An order a channel took and charged for that Tendero could not receive (P16-29). The
/// reconciliation's mark moves past it, so one order never mended cannot hold every other
/// back; this is what remembers it, for the next passes to read it again by its id and
/// for the shopkeeper to see. It leaves only by being received or closed on the channel —
/// the money was taken, and hiding it here would make the list lie.
/// </summary>
public sealed class HeldChannelOrder : AggregateRoot
{
    private HeldChannelOrder()
    {
    }

    public string Channel { get; private set; } = "";

    /// <summary>The channel's id for the order, which it is read again by.</summary>
    public string ExternalId { get; private set; } = "";

    /// <summary>The number the channel's customer and shopkeeper know it by (<c>#1013</c>).</summary>
    public string Number { get; private set; } = "";

    public HeldOrderReason Reason { get; private set; }

    /// <summary>What the reason concerns, when one thing does: the SKU the catalogue does not sell.</summary>
    public string? About { get; private set; }

    /// <summary>The order's page in the channel's own admin, where it can be mended or closed; null when the channel has none.</summary>
    public string? Page { get; private set; }

    public DateTimeOffset HeldSince { get; private set; }

    public DateTimeOffset LastTriedAt { get; private set; }

    public static HeldChannelOrder Hold(
        string channel, string externalId, string number, HeldOrderReason reason, string? about, string? page, DateTimeOffset at)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(channel);
        ArgumentException.ThrowIfNullOrWhiteSpace(externalId);
        ArgumentException.ThrowIfNullOrWhiteSpace(number);
        return new HeldChannelOrder
        {
            Channel = channel,
            ExternalId = externalId,
            Number = number,
            Reason = reason,
            About = about,
            Page = page,
            HeldSince = at,
            LastTriedAt = at,
        };
    }

    /// <summary>Read again and still not received: why may have changed; since when has not.</summary>
    public void StillHeld(HeldOrderReason reason, string? about, DateTimeOffset at)
    {
        Reason = reason;
        About = about;
        LastTriedAt = at;
    }
}
