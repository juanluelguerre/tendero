using ElGuerre.Tendero.Channels.Domain;
using ElGuerre.Tendero.Channels.Ports;
using Microsoft.EntityFrameworkCore;

namespace ElGuerre.Tendero.Persistence;

/// <summary>Channels' state (ADR 0070): the ports are Channels', the adapters these.</summary>
internal sealed class EfChannelListings(TenderoDbContext context) : IChannelListings
{
    public Task<ChannelListing?> FindAsync(string channel, string code, CancellationToken cancellationToken = default) =>
        context.ChannelListings.FirstOrDefaultAsync(listing => listing.Channel == channel && listing.Code == code, cancellationToken);

    /// <summary>
    /// By a key of the jsonb: Postgres' <c>?</c> on <c>variant_ids</c>. A lab's catalogue
    /// is a hundred listings; the index this would want is deferred with its number
    /// (the scale rule).
    /// </summary>
    public Task<ChannelListing?> HoldingAsync(string channel, string sku, CancellationToken cancellationToken = default) =>
        context.ChannelListings
            .FromSql($"SELECT * FROM channels.listings WHERE channel = {channel} AND variant_ids ? {sku}")
            .FirstOrDefaultAsync(cancellationToken);

    public void Add(ChannelListing listing) => context.ChannelListings.Add(listing);
}

internal sealed class EfChannelStockLevels(TenderoDbContext context) : IChannelStockLevels
{
    public Task<ChannelStockLevel?> FindAsync(string channel, string sku, string location, CancellationToken cancellationToken = default) =>
        context.ChannelStockLevels.FirstOrDefaultAsync(
            level => level.Channel == channel && level.Sku == sku && level.Location == location, cancellationToken);

    public void Add(ChannelStockLevel level) => context.ChannelStockLevels.Add(level);
}

internal sealed class EfChannelCursors(TenderoDbContext context) : IChannelCursors
{
    public Task<ChannelCursor?> FindAsync(string channel, CancellationToken cancellationToken = default) =>
        context.ChannelCursors.FirstOrDefaultAsync(cursor => cursor.Channel == channel, cancellationToken);

    public void Add(ChannelCursor cursor) => context.ChannelCursors.Add(cursor);
}

internal sealed class EfHeldChannelOrders(TenderoDbContext context) : IHeldChannelOrders
{
    public async Task<IReadOnlyList<HeldChannelOrder>> OfAsync(string channel, CancellationToken cancellationToken = default) =>
        await context.HeldChannelOrders.Where(order => order.Channel == channel).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<HeldChannelOrder>> AllAsync(CancellationToken cancellationToken = default) =>
        await context.HeldChannelOrders.AsNoTracking().OrderBy(order => order.HeldSince).ToListAsync(cancellationToken);

    public void Add(HeldChannelOrder order) => context.HeldChannelOrders.Add(order);

    public void Remove(HeldChannelOrder order) => context.HeldChannelOrders.Remove(order);
}
