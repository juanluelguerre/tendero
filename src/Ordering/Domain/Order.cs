using ElGuerre.Tendero.SharedKernel;

namespace ElGuerre.Tendero.Ordering.Domain;

public enum OrderStatus
{
    Pending,            // created, awaiting payment authorization
    PaymentAuthorized,  // the payment provider authorized the charge
    PaymentFailed,      // declined; it can be retried or cancelled
    Confirmed,          // payment captured and stock committed
    Shipped,
    Delivered,
    Cancelled
}

// A snapshot: the order keeps the name and price as of the moment of purchase,
// never a live FK to the product (which can change or be archived).
/// <summary>
/// A snapshot of what was bought. It carries the variant and its SKU because
/// **what gets bought is a variant** (ADR 0015): without them an order cannot
/// say which size was shipped, and inventory — which speaks in SKUs — has
/// nothing to decrement.
///
/// <c>VariantLabel</c> is frozen just like <c>ProductName</c>: it is the text the
/// buyer saw ("azul marino · 38"), and reordering the catalogue's axes afterwards
/// must not rewrite their order.
/// </summary>
public sealed record OrderLine(
    ProductId ProductId,
    VariantId VariantId,
    string Sku,
    string ProductName,
    string? VariantLabel,

    /// <summary>
    /// The picture as it was, snapshotted like the name and the price beside it.
    ///
    /// **It is a snapshot and not a lookup**, which is ADR 0002 a fifth time. An
    /// order that resolved the image through the catalogue would show today's
    /// photograph of a product somebody bought last year, and the catalogue is
    /// entitled to change it — that is what a catalogue is for.
    ///
    /// Nullable, because an order placed before this existed has none and a
    /// product may simply have no picture. The interface already draws a
    /// labelled tile for that, which is its default path on a fresh clone.
    /// </summary>
    string? ImageId,

    Money UnitPrice,
    int Quantity)
{
    public Money Total => UnitPrice * Quantity;
}

public sealed record OrderPlaced(OrderId OrderId, DateTimeOffset OccurredAt) : IDomainEvent;
public sealed record OrderPaymentAuthorized(OrderId OrderId, DateTimeOffset OccurredAt) : IDomainEvent;
public sealed record OrderPaymentFailed(OrderId OrderId, string Reason, DateTimeOffset OccurredAt) : IDomainEvent;
public sealed record OrderConfirmed(OrderId OrderId, DateTimeOffset OccurredAt) : IDomainEvent;
public sealed record OrderCancelled(OrderId OrderId, string Reason, DateTimeOffset OccurredAt) : IDomainEvent;

/// <summary>
/// Why an order stopped, in the shape `AppliedDiscount` already established: a
/// CODE the interface localizes, and a DETAIL carrying the specifics.
///
/// **The split is the whole point.** A code alone says "out of stock" and hides
/// which of the two problems happened; a sentence alone cannot be translated,
/// and this one is read by a shopper. So the code answers *what kind* and the
/// detail answers *which SKU and how many* — the second is names and numbers,
/// which read the same in both languages.
///
/// It is stored on the order rather than left in the event, and that is the bug
/// this fixes: the reason travelled in `OrderCancelled`, the outbox marked the
/// message processed, and the order was left saying `Cancelled` and nothing
/// else. A system that refuses has to say so where the refusal is read.
/// </summary>
public sealed record OrderStop(string Code, string? Detail)
{
    /// <summary>The shelf could not fill it. Raised by the stock saga.</summary>
    public const string OutOfStock = "out_of_stock";

    /// <summary>A shopkeeper cancelled it from the backoffice.</summary>
    public const string ShopCancelled = "shop_cancelled";

    /// <summary>The card said no.</summary>
    public const string PaymentDeclined = "payment_declined";
}
public sealed record OrderShipped(OrderId OrderId, DateTimeOffset OccurredAt) : IDomainEvent;

/// <summary>
/// Delivery is a fact too. It was the only transition that emitted nothing, and
/// it turns out to be exactly the one that opens the returns window: phase 4's
/// return-reason loop has nothing else to hang from.
/// </summary>
public sealed record OrderDelivered(OrderId OrderId, DateTimeOffset OccurredAt) : IDomainEvent;

/// <summary>The money actually moved. Distinct from
/// <see cref="OrderPaymentAuthorized"/> because authorising and capturing are
/// two events at the provider and two facts here: a hold that was never taken is
/// released, not refunded.</summary>
public sealed record OrderPaymentCaptured(
    OrderId OrderId, string Provider, string CaptureId, DateTimeOffset OccurredAt) : IDomainEvent;

public sealed class Order : AggregateRoot
{
    // A declarative state machine: a transition outside this table is a bug.
    private static readonly TransitionTable<OrderStatus> AllowedTransitions = new()
    {
        [OrderStatus.Pending] = [OrderStatus.PaymentAuthorized, OrderStatus.PaymentFailed, OrderStatus.Cancelled],
        [OrderStatus.PaymentAuthorized] = [OrderStatus.Confirmed, OrderStatus.Cancelled],
        [OrderStatus.PaymentFailed] = [OrderStatus.Pending, OrderStatus.Cancelled],
        [OrderStatus.Confirmed] = [OrderStatus.Shipped, OrderStatus.Cancelled],
        [OrderStatus.Shipped] = [OrderStatus.Delivered],
        [OrderStatus.Delivered] = [],
        [OrderStatus.Cancelled] = []
    };

    private readonly List<OrderLine> _lines = [];
    private readonly List<OrderDiscount> _discounts = [];
    private readonly List<OrderTax> _taxes = [];

    public OrderId Id { get; private set; }
    public CustomerId CustomerId { get; private set; }

    // The storefront (or the agent over UCP) sends this key: retrying checkout
    // with the same key does NOT create a second order.
    public string IdempotencyKey { get; private set; } = default!;

    // The buyer's language at the time of purchase. The lines keep the name
    // ALREADY resolved in this culture: an order's history does not change if the
    // catalogue gets retranslated.
    public string Culture { get; private set; } = "es";

    // The currency belongs to the order, not to each line: it is fixed at
    // purchase and does not change. Having it here is what lets Total exist even
    // when no lines are left.
    public string Currency { get; private set; } = default!;

    public OrderStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>
    /// Where the parcel goes, frozen. A value and not a pointer into an address
    /// book: editing your address next year must not rewrite where last month's
    /// parcel went.
    /// </summary>
    public Address ShippingAddress { get; private set; } = default!;

    /// <summary>Who is billed. It is its own copy even when it is the same
    /// address, because "same today" is not "same forever".</summary>
    public Address BillingAddress { get; private set; } = default!;

    public OrderShipping Shipping { get; private set; } = default!;

    /// <summary>The quote this order closed against, id and fingerprint both
    /// (ADR 0016).</summary>
    public OrderQuote Quote { get; private set; } = default!;

    public OrderTotals Totals { get; private set; } = default!;

    /// <summary>
    /// What came off, frozen as codes and labels. Backed by a list for the same
    /// reason the lines are: EF maps a private collection into one JSON column,
    /// and the aggregate keeps the only way to add to it.
    /// </summary>
    public IReadOnlyList<OrderDiscount> Discounts => _discounts;

    public IReadOnlyList<OrderTax> Taxes => _taxes;

    /// <summary>Null until a provider authorises. It is the only thing that can
    /// be refunded, which is why a return checks it rather than the status.</summary>
    public OrderPayment? Payment { get; private set; }

    /// <summary>
    /// Set when the order stops — cancelled, or payment failed — and null the
    /// rest of the time, because an order in flight has nothing to explain.
    /// </summary>
    public OrderStop? Stop { get; private set; }

    public IReadOnlyList<OrderLine> Lines => _lines;

    /// <summary>
    /// What the lines add up to before anything is applied. It is NOT the order
    /// total, and it used to be — which was harmless while an order had no
    /// discounts, no shipping and no tax, and became wrong the moment it did.
    /// </summary>
    public Money LinesTotal => _lines.Aggregate(
        Money.Zero(Currency),
        (sum, line) => sum + line.Total);

    /// <summary>
    /// What the buyer agreed to pay. Frozen from the quote and never recomputed:
    /// an order whose total drifted from what was authorised is a chargeback,
    /// not a rounding difference.
    /// </summary>
    public Money Total => Totals.Total;

    /// <summary>
    /// How many of a SKU this order is for. Inventory speaks in SKUs and knows
    /// nothing about lines, so the saga asks the order this rather than grouping
    /// lines itself — and the same question is asked again when a return
    /// restocks.
    /// </summary>
    public IReadOnlyList<(string Sku, int Quantity)> SkuQuantities() =>
    [
        .. _lines
            .GroupBy(line => line.Sku, StringComparer.OrdinalIgnoreCase)
            .Select(group => (Sku: group.Key, Quantity: group.Sum(line => line.Quantity)))
    ];

    private Order() { } // EF Core

    /// <summary>
    /// Places an order.
    ///
    /// Everything monetary arrives as one <paramref name="totals"/> value rather
    /// than being computed here, and that is the point of ADR 0016: `Pricing`
    /// decided these numbers, checkout revalidated the fingerprint that says
    /// they still hold, and `Ordering` freezes them. An aggregate that recomputed
    /// its own total would be a second pricing engine, and the day the two
    /// disagreed the customer would be right and both would be wrong.
    /// </summary>
    public static Order Place(
        TimeProvider clock,
        CustomerId customerId,
        string idempotencyKey,
        IReadOnlyList<OrderLine> lines,
        Address shippingAddress,
        Address billingAddress,
        OrderShipping shipping,
        OrderQuote quote,
        OrderTotals totals,
        IReadOnlyList<OrderDiscount>? discounts = null,
        IReadOnlyList<OrderTax>? taxes = null,
        string culture = "es")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);
        ArgumentNullException.ThrowIfNull(shippingAddress);
        ArgumentNullException.ThrowIfNull(billingAddress);
        ArgumentNullException.ThrowIfNull(shipping);
        ArgumentNullException.ThrowIfNull(quote);
        ArgumentNullException.ThrowIfNull(totals);

        if (lines.Count == 0)
            throw new InvalidOperationException("An order requires at least one line.");
        if (lines.Any(l => l.Quantity <= 0))
            throw new InvalidOperationException("Line quantity must be positive.");

        // An order has ONE currency. Catching it here rather than while summing
        // turns a data error into a named rejection, in the one place that can
        // decide it. Multi-currency is deferred on purpose (initial-plan §7).
        var currency = lines[0].UnitPrice.Currency;
        if (lines.Any(l => !string.Equals(l.UnitPrice.Currency, currency, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("An order cannot mix currencies.");

        // The totals and the shipping come from elsewhere, so their currency is
        // an assumption until it is checked. A total in the wrong currency is
        // the kind of thing that reads fine and charges a hundred times over.
        if (!string.Equals(totals.Total.Currency, currency, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(shipping.Amount.Currency, currency, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"The order is in {currency} and its totals are not.");

        var now = clock.GetUtcNow();
        var order = new Order
        {
            Id = OrderId.New(),
            CustomerId = customerId,
            IdempotencyKey = idempotencyKey,
            Culture = SharedKernel.Culture.Normalize(culture),
            Currency = currency,
            Status = OrderStatus.Pending,
            CreatedAt = now,
            UpdatedAt = now,
            ShippingAddress = shippingAddress,
            BillingAddress = billingAddress,
            Shipping = shipping,
            Quote = quote,
            Totals = totals
        };
        order._lines.AddRange(lines);
        order._discounts.AddRange(discounts ?? []);
        order._taxes.AddRange(taxes ?? []);
        order.Raise(new OrderPlaced(order.Id, now));
        return order;
    }

    /// <summary>
    /// A provider is holding the money. The reference travels with the
    /// provider name, because a reference only means something to the system
    /// that minted it.
    /// </summary>
    public void AuthorizePayment(TimeProvider clock, OrderPayment? payment = null)
    {
        Payment = payment ?? Payment;
        TransitionTo(clock, OrderStatus.PaymentAuthorized, now => new OrderPaymentAuthorized(Id, now));
    }

    /// <summary>
    /// The money moved. It is deliberately NOT a state transition: capturing
    /// happens while an order is `PaymentAuthorized` or already `Confirmed`,
    /// depending on which webhook lands first, and inventing a status for it
    /// would double the table for a fact the payment record already carries.
    /// </summary>
    public void CapturePayment(TimeProvider clock, string captureId)
    {
        if (Payment is null)
            throw new InvalidOperationException($"Order {Id} has no authorization to capture.");

        // At-least-once: a redelivered webhook is the normal case, and the
        // second delivery must not raise a second event.
        if (Payment.CaptureId is not null)
            return;

        Payment = Payment.Captured(captureId);
        UpdatedAt = clock.GetUtcNow();

        Raise(new OrderPaymentCaptured(Id, Payment.Provider, captureId, UpdatedAt));
    }

    public void FailPayment(TimeProvider clock, string reason)
    {
        Stop = new OrderStop(OrderStop.PaymentDeclined, reason);
        TransitionTo(clock, OrderStatus.PaymentFailed, now => new OrderPaymentFailed(Id, reason, now));
    }

    public void Confirm(TimeProvider clock) =>
        TransitionTo(clock, OrderStatus.Confirmed, now => new OrderConfirmed(Id, now));

    public void Ship(TimeProvider clock) =>
        TransitionTo(clock, OrderStatus.Shipped, now => new OrderShipped(Id, now));

    public void Deliver(TimeProvider clock) =>
        TransitionTo(clock, OrderStatus.Delivered, now => new OrderDelivered(Id, now));

    // The compensation saga calls here when something fails halfway (payment
    // authorized but no stock, say): it cancels and releases.
    //
    // The code is required and the detail is not, which is deliberate: every
    // caller knows what KIND of refusal it is, and only some can say more.
    public void Cancel(TimeProvider clock, string code, string? detail = null)
    {
        Stop = new OrderStop(code, detail);
        TransitionTo(clock, OrderStatus.Cancelled,
            now => new OrderCancelled(Id, detail ?? code, now));
    }

    // The factory stopped being nullable when Deliver() started emitting its
    // event: the `IDomainEvent?` existed for one mute transition.
    private void TransitionTo(
        TimeProvider clock, OrderStatus target, Func<DateTimeOffset, IDomainEvent> eventFactory)
    {
        AllowedTransitions.EnsureAllowed(Status, target, $"order {Id}");

        Status = target;
        UpdatedAt = clock.GetUtcNow();

        Raise(eventFactory(UpdatedAt));
    }
}
