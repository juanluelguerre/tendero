using ElGuerre.Tendero.Channels.Domain;

namespace ElGuerre.Tendero.Channels.Ports;

/// <summary>
/// What each channel has been told (ADR 0070). The ports are Channels' and the
/// adapters Persistence's, as Pricing's campaigns work: Channels does not know EF
/// Core exists. Each read is tracked — the slices load, change and save a unit.
/// </summary>
public interface IChannelListings
{
    Task<ChannelListing?> FindAsync(string channel, string code, CancellationToken cancellationToken = default);

    /// <summary>The listing that holds a variant the channel knows by <paramref name="sku"/>; null when none does.</summary>
    Task<ChannelListing?> HoldingAsync(string channel, string sku, CancellationToken cancellationToken = default);

    void Add(ChannelListing listing);
}

public interface IChannelStockLevels
{
    Task<ChannelStockLevel?> FindAsync(string channel, string sku, string location, CancellationToken cancellationToken = default);

    void Add(ChannelStockLevel level);
}

public interface IChannelCursors
{
    Task<ChannelCursor?> FindAsync(string channel, CancellationToken cancellationToken = default);

    void Add(ChannelCursor cursor);
}

/// <summary>The orders a channel took that Tendero could not receive, held to be read again (P16-29).</summary>
public interface IHeldChannelOrders
{
    /// <summary>One channel's, tracked: the reconciliation tries each again and changes or removes it.</summary>
    Task<IReadOnlyList<HeldChannelOrder>> OfAsync(string channel, CancellationToken cancellationToken = default);

    /// <summary>Every channel's, oldest held first, untracked: what the shopkeeper is shown.</summary>
    Task<IReadOnlyList<HeldChannelOrder>> AllAsync(CancellationToken cancellationToken = default);

    void Add(HeldChannelOrder order);

    void Remove(HeldChannelOrder order);
}
