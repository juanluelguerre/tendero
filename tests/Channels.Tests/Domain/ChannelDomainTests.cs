using ElGuerre.Tendero.Channels;
using ElGuerre.Tendero.Channels.Domain;
using Xunit;

namespace ElGuerre.Tendero.Channels.Tests.Domain;

/// <summary>What a channel has been told, and what it may be told next (ADR 0070).</summary>
public sealed class StockMarginTests
{
    /// <summary>The margin is held back from what the channel may sell, per variant (the author kept 2).</summary>
    [Theory]
    [InlineData(5, 2, 3)]
    [InlineData(3, 2, 1)]
    [InlineData(2, 2, 0)]
    [InlineData(1, 2, 0)]
    [InlineData(0, 2, 0)]
    [InlineData(5, 0, 5)]
    public void The_channel_is_told_available_minus_the_margin_and_never_less_than_zero(int available, int margin, int told) =>
        Assert.Equal(told, new StockMargin(margin).ToPublish(available));

    [Fact]
    public void A_negative_margin_is_refused() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new StockMargin(-1));

    [Fact]
    public void The_shop_default_is_two() => Assert.Equal(2, StockMargin.Default.Units);
}

public sealed class ChannelListingTests
{
    private static readonly DateTimeOffset At = new(2026, 10, 30, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_listing_starts_unpublished_and_remembers_what_the_channel_answered()
    {
        var listing = ChannelListing.For("shopify", "TAZ-001");

        listing.Published(
            "gid://shopify/Product/1",
            new Dictionary<string, string> { ["TAZ-001-BLU"] = "gid://shopify/ProductVariant/11" },
            ChannelProductStatus.OnSale,
            At);

        Assert.Equal("gid://shopify/Product/1", listing.ExternalProductId);
        Assert.Equal("gid://shopify/ProductVariant/11", listing.VariantIdOf("TAZ-001-BLU"));
        Assert.Null(listing.VariantIdOf("TAZ-001-RED"));
        Assert.Equal(ChannelProductStatus.OnSale, listing.Status);
        Assert.Equal(ChannelListingOutcome.Published, listing.Outcome);
        Assert.Equal(At, listing.PublishedAt);
        Assert.Null(listing.Refusal);
    }

    /// <summary>A refusal keeps what was published before: the channel still holds it.</summary>
    [Fact]
    public void A_refusal_is_recorded_and_keeps_the_last_publication()
    {
        var listing = ChannelListing.For("shopify", "TAZ-001");
        listing.Published("p1", new Dictionary<string, string>(), ChannelProductStatus.OnSale, At);

        listing.Refused("Shopify said: handle already taken", At.AddMinutes(1));

        Assert.Equal(ChannelListingOutcome.Refused, listing.Outcome);
        Assert.Equal("Shopify said: handle already taken", listing.Refusal);
        Assert.Equal("p1", listing.ExternalProductId);
        Assert.Equal(At, listing.PublishedAt);
    }

    [Fact]
    public void Publishing_again_clears_the_refusal()
    {
        var listing = ChannelListing.For("shopify", "TAZ-001");
        listing.Refused("down", At);

        listing.Published("p1", new Dictionary<string, string>(), ChannelProductStatus.NotOnSale, At.AddMinutes(1));

        Assert.Equal(ChannelListingOutcome.Published, listing.Outcome);
        Assert.Null(listing.Refusal);
    }

    [Theory]
    [InlineData("", "TAZ-001")]
    [InlineData("shopify", " ")]
    public void A_listing_needs_a_channel_and_a_code(string channel, string code) =>
        Assert.Throws<ArgumentException>(() => ChannelListing.For(channel, code));
}

public sealed class ChannelStockLevelTests
{
    private static readonly DateTimeOffset At = new(2026, 10, 30, 12, 0, 0, TimeSpan.Zero);

    /// <summary>Before the first publication nothing is expected: the channel is told its first number.</summary>
    [Fact]
    public void Nothing_is_expected_before_the_first_publication()
    {
        var level = ChannelStockLevel.For("shopify", "TAZ-001-BLU", "main");

        Assert.Null(level.Expected);
    }

    [Fact]
    public void What_was_set_is_what_the_next_publication_expects()
    {
        var level = ChannelStockLevel.For("shopify", "TAZ-001-BLU", "main");

        level.Set(3, At);

        Assert.Equal(3, level.Expected);
        Assert.Equal(At, level.PublishedAt);
        Assert.Null(level.StaleSince);
    }

    /// <summary>
    /// Stale is the channel's sale on its way: Tendero waits for the order rather than
    /// overwrite, and learns what the channel holds now, so the next try expects it.
    /// </summary>
    [Fact]
    public void A_stale_answer_takes_the_channels_number_as_the_next_expected()
    {
        var level = ChannelStockLevel.For("shopify", "TAZ-001-BLU", "main");
        level.Set(3, At);

        level.WentStale(2, At.AddMinutes(1));

        Assert.Equal(2, level.Expected);
        Assert.Equal(At.AddMinutes(1), level.StaleSince);
    }

    [Fact]
    public void A_stale_answer_without_a_number_keeps_the_expected_one()
    {
        var level = ChannelStockLevel.For("shopify", "TAZ-001-BLU", "main");
        level.Set(3, At);

        level.WentStale(null, At.AddMinutes(1));

        Assert.Equal(3, level.Expected);
    }

    [Fact]
    public void Setting_again_clears_the_staleness()
    {
        var level = ChannelStockLevel.For("shopify", "TAZ-001-BLU", "main");
        level.WentStale(1, At);

        level.Set(4, At.AddMinutes(1));

        Assert.Null(level.StaleSince);
        Assert.Equal(4, level.Expected);
    }

    [Fact]
    public void A_negative_quantity_is_refused() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => ChannelStockLevel.For("shopify", "S", "main").Set(-1, At));
}

public sealed class ChannelCursorTests
{
    private static readonly DateTimeOffset At = new(2026, 10, 30, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_cursor_starts_where_it_is_told_and_only_moves_forward()
    {
        var cursor = ChannelCursor.For("shopify", At);

        cursor.ReadUntil(At.AddMinutes(5));
        cursor.ReadUntil(At.AddMinutes(2));

        Assert.Equal(At.AddMinutes(5), cursor.OrdersReadUntil);
    }
}
