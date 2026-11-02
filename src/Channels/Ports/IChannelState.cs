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
