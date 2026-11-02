using ElGuerre.Tendero.SharedKernel;

namespace ElGuerre.Tendero.Channels.Ports;

/// <summary>The channels registered, by key (ADR 0003): a channel configured is a channel published to.</summary>
public interface ISalesChannels
{
    IReadOnlyCollection<string> Channels { get; }

    ISalesChannel Get(string channel);
}

/// <summary>
/// A sales channel (phase 16, S3; ADR 0070): a second place where people buy, kept in
/// step with Tendero, which stays the system of record. The outbound twin of the
/// catalogue's inbound port: Tendero publishes its products and its stock, and reads
/// then tells it when an order it took was shipped — the orders themselves Tendero
/// receives through Ordering's inbound order port, a channel being a source too
/// (ADR 0070). Values only — a channel adapter never sees a
/// domain, which an architecture rule holds — and each adapter registers keyed by
/// <see cref="Channel"/>, so a second channel is a class and a registration line.
/// </summary>
public interface ISalesChannel
{
    /// <summary>A stable lowercase key: <c>shopify</c>.</summary>
    string Channel { get; }

    /// <summary>
    /// Creates or updates the product, idempotent by its code: publishing the same
    /// values twice leaves the channel as once. A product not on sale reaches the
    /// channel as not on sale, never on its shelf; an archived one is archived there.
    /// </summary>
    Task<ChannelListingIds> PublishAsync(ChannelProduct product, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets what the channel may sell of one SKU at one location, as an absolute
    /// quantity, only if the channel still holds <see cref="ChannelStock.Expected"/>:
    /// a channel that sold something since answers <see cref="StockPublication.Stale"/>
    /// and keeps its own number, because the sale's order is on its way.
    /// </summary>
    Task<StockPublication> SetAvailableAsync(ChannelStock stock, CancellationToken cancellationToken = default);

    /// <summary>Tells the channel an order of its was shipped, so its customer hears it from the channel.</summary>
    Task MarkShippedAsync(ChannelShipment shipment, CancellationToken cancellationToken = default);
}

/// <param name="Code">The product's public code (ADR 0026): how the channel and Tendero recognise it.</param>
/// <param name="Pictures">The gallery in its order, by the image store's ids; the adapter makes them reachable.</param>
public sealed record ChannelProduct(
    string Code,
    ChannelProductStatus Status,
    string Name,
    string? Description,
    string? Brand,
    IReadOnlyList<ChannelPicture> Pictures,
    IReadOnlyList<ChannelVariant> Variants)
{
    /// <summary>
    /// A product gone from the catalogue: only its code and its status travel, because the
    /// channel archives what it already holds and needs nothing else to do it.
    /// </summary>
    public static ChannelProduct Archived(string code) => new(code, ChannelProductStatus.Archived, "", null, null, [], []);
}

public sealed record ChannelPicture(string ImageId, string? Alt);

/// <param name="Price">What the channel sells it for, VAT included, as every price in Tendero (ADR 0028).</param>
/// <param name="Options">What it varies by, in the product's order of axes.</param>
public sealed record ChannelVariant(string Sku, string? Barcode, Money Price, IReadOnlyList<ChannelOption> Options);

public sealed record ChannelOption(string Axis, string Value);

/// <summary>The channel's own ids for a product and each of its variants, by SKU: what orders and stock are matched by.</summary>
public sealed record ChannelListingIds(string ProductId, IReadOnlyDictionary<string, string> VariantIdsBySku);

/// <param name="Location">The channel's location: a Tendero warehouse, mapped by the adapter's configuration.</param>
/// <param name="Available">What the channel may sell: Tendero's available minus the margin, never below zero.</param>
/// <param name="Expected">What Tendero last published here; null before the first publication.</param>
/// <param name="IdempotencyKey">Unique per publication, so a retry of the same one changes nothing twice.</param>
public sealed record ChannelStock(string Sku, string Location, int Available, int? Expected, string IdempotencyKey);

/// <summary>What publishing a quantity came to.</summary>
public closed record StockPublication
{
    private StockPublication()
    {
    }

    /// <summary>The channel holds the quantity now.</summary>
    public sealed record Set : StockPublication;

    /// <summary>The channel moved since the last publication — it sold — and kept its own number.</summary>
    public sealed record Stale(int? Actual) : StockPublication;

    /// <summary>The channel would not take it: an unknown SKU or location, a refusal of its own.</summary>
    public sealed record Refused(string Reason) : StockPublication;
}

/// <param name="OrderExternalId">The channel's id for the order.</param>
public sealed record ChannelShipment(string OrderExternalId, string? Carrier, string? TrackingNumber);

/// <summary>
/// The channel will not take what it was sent — a misconfigured store, an answer of
/// its own refusing the product — and retrying the same thing will not change that.
/// The slice records it on the listing; anything else a channel throws is a failure
/// the outbox retries.
/// </summary>
public sealed class ChannelRefusedException(string reason) : Exception(reason);
