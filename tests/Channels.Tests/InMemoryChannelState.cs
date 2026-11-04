using ElGuerre.Tendero.Channels.Domain;
using ElGuerre.Tendero.Channels.Ports;
using ElGuerre.Tendero.SharedKernel;

namespace ElGuerre.Tendero.Channels.Tests;

/// <summary>Channels' state in memory, and a unit of work that counts its saves: the slices' fakes.</summary>
internal sealed class InMemoryChannelState : IChannelListings, IChannelStockLevels, IChannelCursors, IHeldChannelOrders, IUnitOfWork
{
    public List<ChannelListing> Listings { get; } = [];
    public List<ChannelStockLevel> Levels { get; } = [];
    public List<ChannelCursor> Cursors { get; } = [];
    public List<HeldChannelOrder> Held { get; } = [];

    public Task<IReadOnlyList<HeldChannelOrder>> OfAsync(string channel, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<HeldChannelOrder>>([.. Held.Where(order => order.Channel == channel)]);

    // In the order added, not by when: the handler is the one that sorts.
    public Task<IReadOnlyList<HeldChannelOrder>> AllAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<HeldChannelOrder>>([.. Held]);

    public void Add(HeldChannelOrder order) => Held.Add(order);

    public void Remove(HeldChannelOrder order) => Held.Remove(order);
    public int Saves { get; private set; }

    public Task<ChannelListing?> FindAsync(string channel, string code, CancellationToken cancellationToken = default) =>
        Task.FromResult(Listings.FirstOrDefault(listing => listing.Channel == channel && listing.Code == code));

    public Task<ChannelListing?> HoldingAsync(string channel, string sku, CancellationToken cancellationToken = default) =>
        Task.FromResult(Listings.FirstOrDefault(listing => listing.Channel == channel && listing.VariantIds.ContainsKey(sku)));

    public void Add(ChannelListing listing) => Listings.Add(listing);

    public Task<ChannelStockLevel?> FindAsync(string channel, string sku, string location, CancellationToken cancellationToken = default) =>
        Task.FromResult(Levels.FirstOrDefault(level => level.Channel == channel && level.Sku == sku && level.Location == location));

    public void Add(ChannelStockLevel level) => Levels.Add(level);

    public Task<ChannelCursor?> FindAsync(string channel, CancellationToken cancellationToken = default) =>
        Task.FromResult(Cursors.FirstOrDefault(cursor => cursor.Channel == channel));

    public void Add(ChannelCursor cursor) => Cursors.Add(cursor);

    public Task SaveChangesAsync(CancellationToken ct)
    {
        Saves++;
        return Task.CompletedTask;
    }
}

/// <summary>The registered channels, as many as a test hands it.</summary>
internal sealed class SalesChannels(params ISalesChannel[] registered) : ISalesChannels
{
    public IReadOnlyCollection<string> Channels => [.. registered.Select(channel => channel.Channel)];

    public ISalesChannel Get(string channel) => registered.Single(registered => registered.Channel == channel);
}

/// <summary>A channel that refuses or fails everything, for the slices' unhappy paths.</summary>
internal sealed class FailingSalesChannel(Exception failure) : ISalesChannel
{
    public string Channel => "memory";

    public Task<ChannelListingIds> PublishAsync(ChannelProduct product, CancellationToken cancellationToken = default) => throw failure;

    public Task<StockPublication> SetAvailableAsync(ChannelStock stock, CancellationToken cancellationToken = default) => throw failure;

    public Task MarkShippedAsync(ChannelShipment shipment, CancellationToken cancellationToken = default) => throw failure;
}
