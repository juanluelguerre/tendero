using ElGuerre.Tendero.Channels.Ports;
using ElGuerre.Tendero.SharedKernel;
using Xunit;

namespace ElGuerre.Tendero.Channels.Tests.Contract;

/// <summary>
/// The sales channel port's executable contract (phase 16, S3; ADR 0070): every
/// channel inherits this class and passes the same suite. Linked as a file into each
/// test project that has one — Channels.Tests for the in-memory channel, Shopify.Tests
/// for Shopify over recorded answers — like the inbound connectors' suites.
/// </summary>
public abstract class SalesChannelContractTests
{
    protected abstract ISalesChannel CreateChannel();

    /// <summary>Where the channel says a product stands, read back its own way; null when it does not hold it.</summary>
    protected abstract Task<ChannelProductStatus?> StatusOnChannelAsync(ISalesChannel channel, string code);

    /// <summary>A SKU and a location the channel can hold stock for once <see cref="Mug"/> is published.</summary>
    protected abstract string Location { get; }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    protected static ChannelProduct Mug(ChannelProductStatus status = ChannelProductStatus.OnSale) => new(
        "TAZ-001",
        status,
        "Taza de gres",
        "Gres esmaltado a mano.",
        "Alfar",
        [new ChannelPicture("taz-001-1", "La taza, de frente")],
        [
            new ChannelVariant("TAZ-001-BLU", "8400000000017", new Money(12.50m, "EUR"), [new ChannelOption("COLOR", "Azul")]),
            new ChannelVariant("TAZ-001-RED", null, new Money(12.50m, "EUR"), [new ChannelOption("COLOR", "Rojo")]),
        ]);

    [Fact]
    public void Channel_is_a_stable_lowercase_key()
    {
        var channel = CreateChannel();

        Assert.False(string.IsNullOrWhiteSpace(channel.Channel));
        Assert.Equal(channel.Channel, channel.Channel.ToLowerInvariant());
        Assert.DoesNotContain(' ', channel.Channel);
    }

    /// <summary>A retry of the outbox, or a reconciliation, publishes again: the channel must end as after once.</summary>
    [Fact]
    public async Task Publishing_twice_is_one_product_with_the_same_ids()
    {
        var channel = CreateChannel();

        var first = await channel.PublishAsync(Mug(), Ct);
        var second = await channel.PublishAsync(Mug(), Ct);

        Assert.Equal(first.ProductId, second.ProductId);
        Assert.Equal(first.VariantIdsBySku.OrderBy(pair => pair.Key), second.VariantIdsBySku.OrderBy(pair => pair.Key));
        Assert.Equal(["TAZ-001-BLU", "TAZ-001-RED"], first.VariantIdsBySku.Keys.Order(StringComparer.Ordinal));
    }

    /// <summary>Draft is the review queue's reason to exist (ADR 0012): no channel's shelf shows one.</summary>
    [Theory]
    [InlineData(ChannelProductStatus.OnSale)]
    [InlineData(ChannelProductStatus.NotOnSale)]
    [InlineData(ChannelProductStatus.Archived)]
    public async Task The_channel_holds_the_status_it_was_given(ChannelProductStatus status)
    {
        var channel = CreateChannel();

        await channel.PublishAsync(Mug(ChannelProductStatus.OnSale), Ct);
        await channel.PublishAsync(Mug(status), Ct);

        Assert.Equal(status, await StatusOnChannelAsync(channel, "TAZ-001"));
    }

    /// <summary>
    /// Archived, a product travels as its code alone: the channel archives what it holds
    /// and keeps its variants, which its past orders still name.
    /// </summary>
    [Fact]
    public async Task A_product_archived_by_its_code_alone_keeps_its_ids_and_variants()
    {
        var channel = CreateChannel();
        var published = await channel.PublishAsync(Mug(), Ct);

        var archived = await channel.PublishAsync(ChannelProduct.Archived("TAZ-001"), Ct);

        Assert.Equal(ChannelProductStatus.Archived, await StatusOnChannelAsync(channel, "TAZ-001"));
        Assert.Equal(published.ProductId, archived.ProductId);
        Assert.Equal(published.VariantIdsBySku.OrderBy(pair => pair.Key), archived.VariantIdsBySku.OrderBy(pair => pair.Key));
    }

    [Fact]
    public async Task Archiving_a_product_the_channel_never_held_is_refused()
    {
        var channel = CreateChannel();

        await Assert.ThrowsAsync<ChannelRefusedException>(() => channel.PublishAsync(ChannelProduct.Archived("NEVER-SEEN"), Ct));
    }

    [Fact]
    public async Task The_first_quantity_is_set_and_the_next_one_needs_the_last()
    {
        var channel = CreateChannel();
        await channel.PublishAsync(Mug(), Ct);

        var first = await channel.SetAvailableAsync(new ChannelStock("TAZ-001-BLU", Location, 3, null, "k1"), Ct);
        var next = await channel.SetAvailableAsync(new ChannelStock("TAZ-001-BLU", Location, 5, 3, "k2"), Ct);

        Assert.IsType<StockPublication.Set>(first);
        Assert.IsType<StockPublication.Set>(next);
    }

    /// <summary>
    /// The channel sold since the last publication: it keeps its number, and the
    /// sale's order — on its way — is what moves Tendero's.
    /// </summary>
    [Fact]
    public async Task A_quantity_the_channel_no_longer_holds_is_stale_and_not_overwritten()
    {
        var channel = CreateChannel();
        await channel.PublishAsync(Mug(), Ct);
        await channel.SetAvailableAsync(new ChannelStock("TAZ-001-BLU", Location, 3, null, "k1"), Ct);

        var stale = await channel.SetAvailableAsync(new ChannelStock("TAZ-001-BLU", Location, 5, 4, "k2"), Ct);
        var after = await channel.SetAvailableAsync(new ChannelStock("TAZ-001-BLU", Location, 2, 3, "k3"), Ct);

        Assert.IsType<StockPublication.Stale>(stale);
        Assert.IsType<StockPublication.Set>(after);
    }

    [Fact]
    public async Task A_sku_the_channel_was_never_given_is_refused()
    {
        var channel = CreateChannel();

        var refused = await channel.SetAvailableAsync(new ChannelStock("NO-SUCH-SKU", Location, 1, null, "k1"), Ct);

        Assert.IsType<StockPublication.Refused>(refused);
    }
}
