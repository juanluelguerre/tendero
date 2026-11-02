using ElGuerre.Tendero.Channels.Domain;
using ElGuerre.Tendero.Channels.Features.PublishStockToChannel;
using ElGuerre.Tendero.Channels.Ports;
using ElGuerre.Tendero.SharedKernel;
using ElGuerre.Tendero.Tests;
using Xunit;

namespace ElGuerre.Tendero.Channels.Tests.Features;

/// <summary>
/// Telling a channel what it may sell (phase 16, S3; ADR 0070): Tendero's available
/// less the margin, sent with what was last published as the expected quantity. A
/// channel that sold since answers stale; Tendero keeps the channel's number and waits
/// for the sale's order rather than overwrite it.
/// </summary>
public sealed class PublishStockToChannelTests
{
    private static readonly DateTimeOffset At = new(2026, 10, 30, 12, 0, 0, TimeSpan.Zero);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<(InMemorySalesChannel Channel, InMemoryChannelState State)> Published()
    {
        var channel = new InMemorySalesChannel();
        await channel.PublishAsync(new ChannelProduct("TAZ-001", ChannelProductStatus.OnSale, "Taza", null, null, [],
            [new ChannelVariant("TAZ-001-BLU", null, new Money(12.5m, "EUR"), [])]), Ct);
        var state = new InMemoryChannelState();
        var listing = ChannelListing.For("memory", "TAZ-001");
        listing.Published("product/TAZ-001", new Dictionary<string, string> { ["TAZ-001-BLU"] = "variant/TAZ-001-BLU" }, ChannelProductStatus.OnSale, At);
        state.Add(listing);
        return (channel, state);
    }

    private static PublishStockToChannelHandler Handler(ISalesChannel channel, InMemoryChannelState state, int margin = 2) =>
        new(new SalesChannels(channel), state, state, state, new StockMargin(margin), new TestClock(At));

    private static PublishStockToChannelCommand Stock(int available) => new("memory", "TAZ-001-BLU", "MAD", available);

    [Fact]
    public async Task The_channel_is_told_the_available_less_the_margin()
    {
        var (channel, state) = await Published();

        var outcome = await Handler(channel, state).HandleAsync(Stock(5), Ct);

        Assert.Equal(StockOutcome.Set, outcome);
        Assert.Equal(3, channel.AvailableOf("TAZ-001-BLU", "MAD"));
        Assert.Equal(3, Assert.Single(state.Levels).Expected);
    }

    [Fact]
    public async Task With_no_more_than_the_margin_the_channel_sells_none()
    {
        var (channel, state) = await Published();

        await Handler(channel, state).HandleAsync(Stock(2), Ct);

        Assert.Equal(0, channel.AvailableOf("TAZ-001-BLU", "MAD"));
    }

    /// <summary>The second publication expects the first: compare-and-swap against what Tendero last told it.</summary>
    [Fact]
    public async Task The_next_publication_expects_what_was_told_last()
    {
        var (channel, state) = await Published();

        await Handler(channel, state).HandleAsync(Stock(5), Ct);
        var outcome = await Handler(channel, state).HandleAsync(Stock(8), Ct);

        Assert.Equal(StockOutcome.Set, outcome);
        Assert.Equal(6, channel.AvailableOf("TAZ-001-BLU", "MAD"));
    }

    /// <summary>
    /// The channel sold one since: it keeps its number, the level learns it, and the next
    /// publication — once the sale's order has moved Tendero's stock — expects it.
    /// </summary>
    [Fact]
    public async Task A_channel_that_sold_since_is_not_overwritten_and_its_number_is_expected_next()
    {
        var (channel, state) = await Published();
        await Handler(channel, state).HandleAsync(Stock(5), Ct);
        channel.Sell("TAZ-001-BLU", "MAD", 1);

        var stale = await Handler(channel, state).HandleAsync(Stock(6), Ct);
        Assert.Equal(StockOutcome.Stale, stale);
        Assert.Equal(2, channel.AvailableOf("TAZ-001-BLU", "MAD"));
        Assert.Equal((2, At), (state.Levels[0].Expected, state.Levels[0].StaleSince));

        var after = await Handler(channel, state).HandleAsync(Stock(3), Ct);
        Assert.Equal(StockOutcome.Set, after);
        Assert.Equal(1, channel.AvailableOf("TAZ-001-BLU", "MAD"));
        Assert.Null(state.Levels[0].StaleSince);
    }

    [Fact]
    public async Task The_same_quantity_again_tells_the_channel_nothing()
    {
        var (channel, state) = await Published();
        await Handler(channel, state).HandleAsync(Stock(5), Ct);
        channel.Sell("TAZ-001-BLU", "MAD", 1);

        var outcome = await Handler(channel, state).HandleAsync(Stock(5), Ct);

        Assert.Equal(StockOutcome.Unchanged, outcome);
        Assert.Equal(2, channel.AvailableOf("TAZ-001-BLU", "MAD"));
    }

    /// <summary>A SKU the channel was never given a product for: there is nothing there to stock.</summary>
    [Fact]
    public async Task A_sku_no_listing_holds_is_not_told()
    {
        var channel = new InMemorySalesChannel();
        var state = new InMemoryChannelState();

        var outcome = await Handler(channel, state).HandleAsync(Stock(5), Ct);

        Assert.Equal(StockOutcome.NotListed, outcome);
        Assert.Empty(state.Levels);
        Assert.Equal(0, state.Saves);
    }

    /// <summary>The channel already holds what a stale mark expected: agreeing with it clears the mark, sending nothing.</summary>
    [Fact]
    public async Task The_channels_own_number_published_again_clears_the_stale_mark()
    {
        var (channel, state) = await Published();
        await Handler(channel, state).HandleAsync(Stock(5), Ct);
        channel.Sell("TAZ-001-BLU", "MAD", 1);
        await Handler(channel, state).HandleAsync(Stock(6), Ct);
        var sent = channel.StockSent.Count;

        var outcome = await Handler(channel, state).HandleAsync(Stock(4), Ct);

        Assert.Equal(StockOutcome.Unchanged, outcome);
        Assert.Null(state.Levels[0].StaleSince);
        Assert.Equal(sent, channel.StockSent.Count);
    }

    /// <summary>
    /// The key names the change — channel, SKU, warehouse, from and to — so a retry of the
    /// same publication is the same key and the channel changes nothing twice, and a new
    /// change is a new key.
    /// </summary>
    [Fact]
    public async Task Each_change_carries_a_key_that_names_it()
    {
        var (channel, state) = await Published();

        await Handler(channel, state).HandleAsync(Stock(5), Ct);
        await Handler(channel, state).HandleAsync(Stock(8), Ct);

        Assert.Equal(
            ["memory:TAZ-001-BLU:MAD:none>3", "memory:TAZ-001-BLU:MAD:3>6"],
            channel.StockSent.Select(stock => stock.IdempotencyKey));
        Assert.Equal(2, state.Saves);
    }

    /// <summary>Only what reached the channel is counted: nothing listed and nothing changed are not publications.</summary>
    [Fact]
    public async Task Publications_are_counted_by_what_the_channel_answered()
    {
        using var recorder = new InstrumentRecorder("tendero.channel.publications");
        var (channel, state) = await Published();

        await Handler(channel, state).HandleAsync(Stock(5), Ct);
        await Handler(channel, state).HandleAsync(Stock(5), Ct);
        channel.Sell("TAZ-001-BLU", "MAD", 1);
        await Handler(channel, state).HandleAsync(Stock(9), Ct);
        await Handler(channel, new InMemoryChannelState()).HandleAsync(Stock(5), Ct);

        Assert.Equal(
            ["published", "stale"],
            recorder.Of("tendero.channel.publications")
                .Where(tags => (string?)tags["kind"] == "stock" && (string?)tags["channel"] == "memory")
                .Select(tags => (string?)tags["outcome"]));
    }

    [Fact]
    public async Task A_refusal_is_counted_and_nothing_is_remembered_as_set()
    {
        var (_, state) = await Published();
        using var recorder = new InstrumentRecorder("tendero.channel.publications");

        var outcome = await Handler(new RefusingChannel(), state).HandleAsync(Stock(5), Ct);

        Assert.Equal(StockOutcome.Refused, outcome);
        Assert.Null(Assert.Single(state.Levels).Expected);
        Assert.Equal(
            [("memory", "stock", "refused")],
            recorder.Of("tendero.channel.publications").Where(tags => (string?)tags["kind"] == "stock").Select(tags => ((string?)tags["channel"], (string?)tags["kind"], (string?)tags["outcome"])));
    }

    private sealed class RefusingChannel : ISalesChannel
    {
        public string Channel => "memory";

        public Task<ChannelListingIds> PublishAsync(ChannelProduct product, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<StockPublication> SetAvailableAsync(ChannelStock stock, CancellationToken cancellationToken = default) =>
            Task.FromResult<StockPublication>(new StockPublication.Refused("No location for MAD."));

        public Task MarkShippedAsync(ChannelShipment shipment, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
