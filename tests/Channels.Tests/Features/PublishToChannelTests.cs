using ElGuerre.Tendero.Channels.Domain;
using ElGuerre.Tendero.Channels.Features.PublishToChannel;
using ElGuerre.Tendero.Channels.Ports;
using ElGuerre.Tendero.SharedKernel;
using ElGuerre.Tendero.Tests;
using Xunit;

namespace ElGuerre.Tendero.Channels.Tests.Features;

/// <summary>
/// Publishing a product to a channel (phase 16, S3; ADR 0070): the channel is told,
/// and the listing remembers what it answered — the ids it gave, or why it refused.
/// A refusal is recorded and not retried; any other failure is the outbox's to retry.
/// </summary>
public sealed class PublishToChannelTests
{
    private static readonly DateTimeOffset At = new(2026, 10, 30, 12, 0, 0, TimeSpan.Zero);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static ChannelProduct Mug(ChannelProductStatus status = ChannelProductStatus.OnSale) => new(
        "TAZ-001", status, "Taza de gres", null, null, [],
        [new ChannelVariant("TAZ-001-BLU", null, new Money(12.5m, "EUR"), [])]);

    private static PublishToChannelHandler Handler(ISalesChannel channel, InMemoryChannelState state) =>
        new(new SalesChannels(channel), state, state, new TestClock(At));

    [Fact]
    public async Task The_channel_is_told_and_the_listing_keeps_its_ids()
    {
        var channel = new InMemorySalesChannel();
        var state = new InMemoryChannelState();

        var outcome = await Handler(channel, state).HandleAsync(new PublishToChannelCommand("memory", Mug()), Ct);

        Assert.Equal(ChannelListingOutcome.Published, outcome);
        Assert.Equal(ChannelProductStatus.OnSale, channel.StatusOf("TAZ-001"));
        var listing = Assert.Single(state.Listings);
        Assert.Equal(("product/TAZ-001", "variant/TAZ-001-BLU", At), (listing.ExternalProductId, listing.VariantIdOf("TAZ-001-BLU"), listing.PublishedAt));
        Assert.Equal(1, state.Saves);
    }

    /// <summary>
    /// The channel is told what Tendero last published the product as, so it updates
    /// that one rather than look for it again (the lab store's round trip, 2026-11-03).
    /// </summary>
    [Fact]
    public async Task The_channel_is_told_what_the_product_was_last_published_as()
    {
        var channel = new InMemorySalesChannel();
        var state = new InMemoryChannelState();

        await Handler(channel, state).HandleAsync(new PublishToChannelCommand("memory", Mug()), Ct);
        await Handler(channel, state).HandleAsync(new PublishToChannelCommand("memory", Mug()), Ct);

        Assert.Equal([null, "product/TAZ-001"], channel.Told.Select(product => product.ListedAs));
    }

    /// <summary>The second publication of the same product updates its listing; it never makes a second one.</summary>
    [Fact]
    public async Task Publishing_again_updates_the_one_listing()
    {
        var channel = new InMemorySalesChannel();
        var state = new InMemoryChannelState();

        await Handler(channel, state).HandleAsync(new PublishToChannelCommand("memory", Mug()), Ct);
        await Handler(channel, state).HandleAsync(new PublishToChannelCommand("memory", Mug(ChannelProductStatus.NotOnSale)), Ct);

        Assert.Equal(ChannelProductStatus.NotOnSale, Assert.Single(state.Listings).Status);
    }

    /// <summary>A draft archived before it was ever published: the channel has nothing to archive and is not asked.</summary>
    [Fact]
    public async Task Archiving_what_was_never_published_tells_the_channel_nothing()
    {
        var state = new InMemoryChannelState();

        var outcome = await Handler(new FailingSalesChannel(new InvalidOperationException("must not be called")), state)
            .HandleAsync(new PublishToChannelCommand("memory", ChannelProduct.Archived("TAZ-001")), Ct);

        Assert.Equal(ChannelListingOutcome.Unpublished, outcome);
        Assert.Empty(state.Listings);
        Assert.Equal(0, state.Saves);
    }

    [Fact]
    public async Task A_refusal_is_recorded_on_the_listing_and_not_thrown()
    {
        var state = new InMemoryChannelState();

        var outcome = await Handler(new FailingSalesChannel(new ChannelRefusedException("taxesIncluded is false")), state)
            .HandleAsync(new PublishToChannelCommand("memory", Mug()), Ct);

        Assert.Equal(ChannelListingOutcome.Refused, outcome);
        Assert.Equal("taxesIncluded is false", Assert.Single(state.Listings).Refusal);
        Assert.Equal(1, state.Saves);
    }

    /// <summary>A store that is down is not a refusal: the outbox retries the event, and nothing is saved meanwhile.</summary>
    [Fact]
    public async Task Any_other_failure_is_thrown_for_the_outbox_to_retry()
    {
        var state = new InMemoryChannelState();

        await Assert.ThrowsAsync<HttpRequestException>(() => Handler(new FailingSalesChannel(new HttpRequestException("503")), state)
            .HandleAsync(new PublishToChannelCommand("memory", Mug()), Ct));

        Assert.Equal(0, state.Saves);
    }

    [Fact]
    public async Task Each_publication_is_counted_by_channel_kind_and_outcome()
    {
        using var recorder = new InstrumentRecorder("tendero.channel.publications");

        await Handler(new InMemorySalesChannel(), new InMemoryChannelState()).HandleAsync(new PublishToChannelCommand("memory", Mug()), Ct);
        await Handler(new FailingSalesChannel(new ChannelRefusedException("no")), new InMemoryChannelState())
            .HandleAsync(new PublishToChannelCommand("memory", Mug()), Ct);

        Assert.Equal(
            [("memory", "product", "published"), ("memory", "product", "refused")],
            // The meter is the process's: the stock slice's tests, running beside these, count too.
            recorder.Of("tendero.channel.publications")
                .Where(tags => (string?)tags["kind"] == "product")
                .Select(tags => ((string?)tags["channel"], (string?)tags["kind"], (string?)tags["outcome"])));
    }
}
