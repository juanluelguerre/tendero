using ElGuerre.Tendero.SharedKernel;

namespace ElGuerre.Tendero.Ordering.Ports;

/// <summary>
/// The orders' inbound port (phase 16, S2b, P16-18; ADR 0066): a store a merchant
/// is leaving hands over the orders its customers placed, in the shop's words — a
/// line by the SKU the catalogue sells it under, money in the order's currency,
/// the customer by the store's id for them. Each source registers keyed by name,
/// as the other ports do, and the ImportOrders slice knows only this interface.
/// </summary>
public interface IOrderSourceConnector
{
    /// <summary>A stable lowercase identifier: "shopify"…</summary>
    string Source { get; }

    IAsyncEnumerable<SourceOrder> StreamOrdersAsync(CancellationToken cancellationToken = default);

    /// <summary>What the source could not hand over cleanly, known once the stream has been read.</summary>
    IReadOnlyList<OrderNote> SourceNotes => [];
}

/// <summary>
/// A sales channel's orders (phase 16, S3; ADR 0070): the orders a channel Tendero
/// publishes to took, read so Tendero can ship them. A channel is a source too, so the
/// orders come as <see cref="SourceOrder"/> through the mapping the import already holds;
/// keyed by the channel's name, like the import's sources.
/// </summary>
public interface IChannelOrderSource
{
    /// <summary>The channel's stable lowercase key: <c>shopify</c>.</summary>
    string Channel { get; }

    /// <summary>The orders changed at or after <paramref name="since"/>, oldest change first.</summary>
    IAsyncEnumerable<SourceOrder> UpdatedSinceAsync(DateTimeOffset since, CancellationToken cancellationToken = default);

    /// <summary>One order by the channel's id for it; null when the channel has none.</summary>
    Task<SourceOrder?> FindAsync(string externalId, CancellationToken cancellationToken = default);
}

/// <summary>The channels Tendero receives orders from, by key (ADR 0003).</summary>
public interface IChannelOrderSources
{
    IChannelOrderSource Get(string channel);
}

/// <summary>Where an order stands at the source: finished, cancelled, or still on its way.</summary>
public enum SourceOrderState
{
    Fulfilled,
    Cancelled,
    Open,
}

/// <param name="ExternalId">The source's id for the order: what a re-import finds it by.</param>
/// <param name="Number">The number the merchant and the buyer knew it by (<c>#1001</c>).</param>
/// <param name="CustomerExternalId">The source's id for the buyer, when it had one: the imported customer it belongs to.</param>
/// <param name="ShippedAt">When it left, for a fulfilled order: the returns window counts from it.</param>
/// <param name="UpdatedAt">When the source last changed it: where a sales channel's reconciliation goes on from (phase 16, S3).</param>
public sealed record SourceOrder(
    string ExternalId,
    string Number,
    string? CustomerExternalId,
    string? ContactEmail,
    string? Culture,
    DateTimeOffset PlacedAt,
    DateTimeOffset? ShippedAt,
    SourceOrderState State,
    IReadOnlyList<SourceOrderLine> Lines,
    Address? ShippingAddress,
    Address? BillingAddress,
    SourceShipping Shipping,
    SourceOrderTotals Totals,
    IReadOnlyList<SourceDiscount> Discounts,
    IReadOnlyList<SourceTax> Taxes,
    IReadOnlyList<SourceRefund> Refunds,
    DateTimeOffset? UpdatedAt = null);

/// <param name="Sku">The SKU the catalogue sells it under; null when the catalogue never had it.</param>
/// <param name="ExternalId">The source's id for the line: what a refund names it by.</param>
public sealed record SourceOrderLine(string ExternalId, string? Sku, string ProductName, string? VariantLabel, Money UnitPrice, int Quantity);

public sealed record SourceShipping(string Label, Money Amount);

/// <summary>The order's figures as the store charged them, taxes included (ADR 0028).</summary>
public sealed record SourceOrderTotals(Money Subtotal, Money Discount, Money Shipping, Money Tax, Money Total);

public sealed record SourceDiscount(string Code, string Label, Money Amount);

/// <param name="Title">What the store called the tax (<c>IVA 21%</c>): the order's history keeps the store's name for it.</param>
public sealed record SourceTax(string Title, decimal Rate, Money Amount);

/// <param name="Lines">What came back, by the line's source id and how many.</param>
public sealed record SourceRefund(string ExternalId, DateTimeOffset At, Money Amount, IReadOnlyList<(string LineExternalId, int Quantity)> Lines);

/// <summary>What did not come across cleanly from an order source.</summary>
public enum OrderNoteCode
{
    /// <summary>An order still on its way: finish it in the store before switching; it was not imported.</summary>
    StillOpen,

    /// <summary>A line names something the catalogue does not have: import the catalogue first; the order was not imported.</summary>
    ProductNotInCatalogue,

    /// <summary>The buyer was not imported (no email, or erased): the order came, owned by a guest.</summary>
    CustomerNotImported,

    /// <summary>The store's figures did not add up as an order's must: it was not imported.</summary>
    TotalsDoNotAddUp,

    /// <summary>A refund the shop could not express as a return (a line it cannot find, money with no line): the order came without it.</summary>
    RefundNotExpressed,
}

/// <param name="About">The order's number at the source, or null for the source as a whole.</param>
public sealed record OrderNote(OrderNoteCode Code, string? About, string? Detail);

/// <summary>The order sources registered, by name; the only place that talks to the container.</summary>
public interface IOrderSourceRegistry
{
    IReadOnlyCollection<string> Sources { get; }

    IOrderSourceConnector Get(string source);
}

/// <summary>
/// The catalogue's variants by SKU, whatever their status (phase 16, S2b): what an
/// imported order's line snapshots. Not <see cref="IPurchasableReader"/>, which
/// answers only what is on sale: an order already placed is not a purchase, and a
/// catalogue just imported is all in Draft (ADR 0012), so the history would wait on
/// the review queue for no reason. Persistence answers it.
/// </summary>
public interface ICatalogueVariants
{
    /// <summary>The ones the catalogue has, keyed by SKU; the others are absent.</summary>
    Task<IReadOnlyDictionary<string, PurchasableVariant>> FindBySkusAsync(
        IReadOnlyCollection<string> skus, CancellationToken cancellationToken = default);
}

/// <summary>
/// The customer an imported order belongs to (phase 16, S2b): the store's id for
/// the buyer, answered with the customer Accounts imported under it. A port, as
/// <see cref="IRegisteredCustomers"/> is, so Ordering never learns what a customer
/// is; Persistence answers it.
/// </summary>
public interface IImportedCustomerIds
{
    /// <summary>The customers imported from this source, by the source's ids; the ones never imported are absent.</summary>
    Task<IReadOnlyDictionary<string, CustomerId>> ForAsync(string source, IReadOnlyCollection<string> externalIds, CancellationToken cancellationToken = default);
}
