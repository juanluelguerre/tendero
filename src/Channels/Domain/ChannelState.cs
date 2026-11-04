using ElGuerre.Tendero.SharedKernel;

namespace ElGuerre.Tendero.Channels.Domain;

/// <summary>
/// What a channel may sell of a variant: Tendero's available less a margin, never
/// below zero (ADR 0070). The margin covers the window between a sale on the channel
/// and its order arriving here, in which the same unit could be sold elsewhere; one
/// number for the whole shop, by the scale rule, and the author kept 2.
/// </summary>
public sealed record StockMargin
{
    public static readonly StockMargin Default = new(2);

    public StockMargin(int units)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(units);
        Units = units;
    }

    public int Units { get; }

    public int ToPublish(int available) => Math.Max(0, available - Units);
}

public enum ChannelListingOutcome
{
    /// <summary>Known to Tendero, not yet sent.</summary>
    Unpublished,

    /// <summary>The channel took the last publication.</summary>
    Published,

    /// <summary>The channel refused the last one; it still holds what it took before.</summary>
    Refused,
}

/// <summary>
/// A product as a channel holds it: the ids the channel gave it and its variants,
/// and how the last publication went. Keyed by the channel and the product's code
/// (ADR 0026), so a product crosses into this context as a value.
/// </summary>
public sealed class ChannelListing : AggregateRoot
{
    private Dictionary<string, string> variantIds = new(StringComparer.Ordinal);

    private ChannelListing()
    {
    }

    public string Channel { get; private set; } = "";

    public string Code { get; private set; } = "";

    public string? ExternalProductId { get; private set; }

    public ChannelProductStatus? Status { get; private set; }

    public ChannelListingOutcome Outcome { get; private set; }

    public DateTimeOffset? PublishedAt { get; private set; }

    /// <summary>Why the channel refused the last publication, in its own words.</summary>
    public string? Refusal { get; private set; }

    public DateTimeOffset? RefusedAt { get; private set; }

    public IReadOnlyDictionary<string, string> VariantIds => variantIds;

    public static ChannelListing For(string channel, string code)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(channel);
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        return new ChannelListing { Channel = channel, Code = code, Outcome = ChannelListingOutcome.Unpublished };
    }

    public string? VariantIdOf(string sku) => variantIds.GetValueOrDefault(sku);

    public void Published(
        string externalProductId, IReadOnlyDictionary<string, string> variantIdsBySku, ChannelProductStatus status, DateTimeOffset at)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(externalProductId);
        ExternalProductId = externalProductId;
        variantIds = new Dictionary<string, string>(variantIdsBySku, StringComparer.Ordinal);
        Status = status;
        Outcome = ChannelListingOutcome.Published;
        PublishedAt = at;
        Refusal = null;
    }

    public void Refused(string reason, DateTimeOffset at)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        Outcome = ChannelListingOutcome.Refused;
        Refusal = reason;
        RefusedAt = at;
    }

}

/// <summary>
/// The stock last published for a SKU at a channel's location: the expected value of
/// the next compare-and-swap. A stale answer means the channel sold since; Tendero
/// does not overwrite, and expects the channel's number next time.
/// </summary>
public sealed class ChannelStockLevel : AggregateRoot
{
    private ChannelStockLevel()
    {
    }

    public string Channel { get; private set; } = "";

    public string Sku { get; private set; } = "";

    public string Location { get; private set; } = "";

    /// <summary>What the channel is believed to hold; null before the first publication.</summary>
    public int? Expected { get; private set; }

    public DateTimeOffset? PublishedAt { get; private set; }

    /// <summary>When the channel last answered stale; cleared by the next publication it takes.</summary>
    public DateTimeOffset? StaleSince { get; private set; }

    public static ChannelStockLevel For(string channel, string sku, string location)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(channel);
        ArgumentException.ThrowIfNullOrWhiteSpace(sku);
        ArgumentException.ThrowIfNullOrWhiteSpace(location);
        return new ChannelStockLevel { Channel = channel, Sku = sku, Location = location };
    }

    public void Set(int quantity, DateTimeOffset at)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(quantity);
        Expected = quantity;
        PublishedAt = at;
        StaleSince = null;
    }

    public void WentStale(int? actual, DateTimeOffset at)
    {
        if (actual is { } held) Expected = held;
        StaleSince = at;
    }
}

/// <summary>Where a channel's reconciliation got to: the orders updated before this mark have been read.</summary>
public sealed class ChannelCursor : AggregateRoot
{
    private ChannelCursor()
    {
    }

    public string Channel { get; private set; } = "";

    public DateTimeOffset OrdersReadUntil { get; private set; }

    public static ChannelCursor For(string channel, DateTimeOffset from)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(channel);
        return new ChannelCursor { Channel = channel, OrdersReadUntil = from };
    }

    /// <summary>Moves forward only: a reconciliation that read less never undoes one that read more.</summary>
    public void ReadUntil(DateTimeOffset mark)
    {
        if (mark > OrdersReadUntil) OrdersReadUntil = mark;
    }
}
