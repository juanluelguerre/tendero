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
    int Quantity,

    /// <summary>
    /// The category the product was sold in, snapshotted like its name (R-9,
    /// D-7): the dashboard's sales by category read it, and a product moved to
    /// another category later does not move last month's sales with it. Null on
    /// a line sold before it was recorded, or of a product with no category.
    /// </summary>
    string? CategoryCode = null,

    /// <summary>
    /// The category's path of codes as it was, <c>HOME/KITCHEN/COOKWARE</c>, so a
    /// figure about a department reaches everything under it — as a promotion on a
    /// department does (Pricing's <c>PathContains</c>). Snapshotted with the code.
    /// </summary>
    string? CategoryPath = null)
{
    public Money Total => UnitPrice * Quantity;

    /// <summary>Whether this line was sold in <paramref name="category"/> or anywhere under it.</summary>
    public bool IsIn(string category) =>
        string.Equals(CategoryCode, category, StringComparison.OrdinalIgnoreCase)
        || (CategoryPath?.Split('/').Contains(category, StringComparer.OrdinalIgnoreCase) ?? false);
}

public sealed record OrderPlaced(OrderId OrderId, DateTimeOffset OccurredAt) : IDomainEvent;
public sealed record OrderPaymentAuthorized(OrderId OrderId, DateTimeOffset OccurredAt) : IDomainEvent;
public sealed record OrderPaymentFailed(OrderId OrderId, string Reason, DateTimeOffset OccurredAt) : IDomainEvent;
public sealed record OrderConfirmed(OrderId OrderId, DateTimeOffset OccurredAt) : IDomainEvent;
public sealed record OrderCancelled(OrderId OrderId, string Reason, DateTimeOffset OccurredAt) : IDomainEvent;

/// <summary>A channel took money for stock Tendero could not hold (phase 16, S3; ADR 0070): a person must act.</summary>
public sealed record OrderOversold(OrderId OrderId, string Reason, DateTimeOffset OccurredAt) : IDomainEvent;

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

    /// <summary>
    /// A test order that went all the way to confirmed, and ends here: it is never
    /// shipped, and cancelling it is what gives its stock back and voids its hold
    /// (ADR 0038).
    /// </summary>
    public const string TestOrderEnded = "test_order_ended";
}

/// <summary>
/// Whether an order is real (ADR 0038, P11-0). A <c>Test</c> order goes through the
/// whole saga — reservation, state machine, audit, events — pays through the test
/// provider, is never shipped and gives its stock back. **Fixed at placement and
/// decided by the server from the credential**, never by the request: a mode an
/// agent could choose is a way to get goods without paying, or to be charged by
/// mistake.
/// </summary>
public enum OrderMode
{
    Live,
    Test,
}

public static class OrderModes
{
    /// <summary>The one rule: a test credential places test orders, every other one live ones.</summary>
    public static OrderMode Of(CommercePrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);
        return principal.IsTest ? OrderMode.Test : OrderMode.Live;
    }
}
public sealed record OrderShipped(OrderId OrderId, DateTimeOffset OccurredAt) : IDomainEvent;

/// <summary>
/// Delivery is a fact too. It was the only transition that emitted nothing, and
/// it turns out to be exactly the one that opens the returns window: returns
/// (phase 5), and the return-reason analysis a later phase promises, have
/// nothing else to hang from.
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
        // Placed here, an order starts Pending; brought from another platform it is
        // history, born in its final state (ADR 0066, Imported).
        Entries = [OrderStatus.Pending, OrderStatus.Delivered, OrderStatus.Cancelled],
        [OrderStatus.Pending] = [OrderStatus.PaymentAuthorized, OrderStatus.PaymentFailed, OrderStatus.Cancelled],
        [OrderStatus.PaymentAuthorized] = [OrderStatus.Confirmed, OrderStatus.Cancelled],
        [OrderStatus.PaymentFailed] = [OrderStatus.Pending, OrderStatus.Cancelled],
        [OrderStatus.Confirmed] = [OrderStatus.Shipped, OrderStatus.Cancelled],
        [OrderStatus.Shipped] = [OrderStatus.Delivered],
        [OrderStatus.Delivered] = [],
        [OrderStatus.Cancelled] = []
    };

    private readonly List<OrderLine> lines = [];
    private readonly List<OrderDiscount> discounts = [];
    private readonly List<OrderTax> taxes = [];

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

    /// <summary>Live or a rehearsal, fixed when the order was placed (ADR 0038).</summary>
    public OrderMode Mode { get; private set; }

    /// <summary>
    /// Where to write to the buyer about this order, when they said: a UCP checkout
    /// asks for it before it completes (P11-1), the storefront does not yet.
    /// Snapshotted like the address — the email a purchase was confirmed to is a
    /// fact about the purchase.
    /// </summary>
    public string? ContactEmail { get; private set; }

    /// <summary>
    /// The mandate an agent placed it under, when there was one (P11-5): null for a
    /// person's order and for an agent's without AP2. Fixed at placement like the mode.
    /// </summary>
    public OrderMandate? Mandate { get; private set; }

    /// <summary>
    /// The agent that placed it, when one did (P11-6): frozen like the mode, so
    /// "how many orders has this agent placed this hour" and "what share of sales do
    /// agents bring" are questions about orders, not about logs.
    /// </summary>
    public AgentId? PlacedBy { get; private set; }

    /// <summary>
    /// The way the order came in — the storefront, MCP or UCP (R-9, D-7). Null
    /// on an order placed before it was recorded; the dashboard counts those
    /// apart and never as the web.
    /// </summary>
    public OrderChannel? Channel { get; private set; }

    /// <summary>
    /// When the order was confirmed and when it shipped, recorded as each
    /// happens (R-9, D-7), because a late shipment is measured between the two
    /// and <see cref="UpdatedAt"/> only knows the last change. Null until then,
    /// and on an order that moved before they were recorded.
    /// </summary>
    public DateTimeOffset? ConfirmedAt { get; private set; }

    public DateTimeOffset? ShippedAt { get; private set; }

    /// <summary>
    /// Where an imported order came from (ADR 0066): the platform, its id there and
    /// the number the merchant knew it by. Null for an order placed here, which is
    /// what lets a figure tell the shop's history from its sales.
    /// </summary>
    public OrderOrigin? ImportedFrom { get; private set; }

    /// <summary>
    /// Where an order a sales channel took came from (phase 16, S3; ADR 0070): the
    /// channel, its id there and the number its customer was told. Unlike an imported
    /// one it is live — Tendero ships it.
    /// </summary>
    public OrderOrigin? ReceivedFrom { get; private set; }

    /// <summary>
    /// Why the stock of an order a channel took could not be held, in the ledger's
    /// words; null the rest of the time. The order is kept, for a person to act on.
    /// </summary>
    public string? Oversold { get; private set; }

    /// <summary>
    /// What came off, frozen as codes and labels. Backed by a list for the same
    /// reason the lines are: EF maps a private collection into one JSON column,
    /// and the aggregate keeps the only way to add to it.
    /// </summary>
    public IReadOnlyList<OrderDiscount> Discounts => this.discounts;

    public IReadOnlyList<OrderTax> Taxes => this.taxes;

    /// <summary>Null until a provider authorises. It is the only thing that can
    /// be refunded, which is why a return checks it rather than the status.</summary>
    public OrderPayment? Payment { get; private set; }

    /// <summary>
    /// Set when the order stops — cancelled, or payment failed — and null the
    /// rest of the time, because an order in flight has nothing to explain.
    /// </summary>
    public OrderStop? Stop { get; private set; }

    public IReadOnlyList<OrderLine> Lines => this.lines;

    /// <summary>
    /// What the lines add up to before anything is applied. It is NOT the order
    /// total, and it used to be — which was harmless while an order had no
    /// discounts, no shipping and no tax, and became wrong the moment it did.
    /// </summary>
    public Money LinesTotal => this.lines.Aggregate(
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
        .. this.lines
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
        string culture = "es",
        OrderMode mode = OrderMode.Live,
        OrderId? id = null,
        string? contactEmail = null,
        OrderMandate? mandate = null,
        AgentId? placedBy = null,
        OrderChannel? channel = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);
        ArgumentNullException.ThrowIfNull(quote);
        AllowedTransitions.EnsureEntry(OrderStatus.Pending, "a placed order");
        var currency = EnsureConsistent(lines, shippingAddress, billingAddress, shipping, totals);

        // Placement checks this before charging anything; the aggregate holds the
        // rule too, so no path — present or future — places an order for other
        // money than a mandate approved.
        if (mandate is not null
            && (mandate.ApprovedTotal != totals.Total.Amount || !string.Equals(mandate.Currency, currency, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException(
                $"The mandate approves {mandate.ApprovedTotal} {mandate.Currency}; this order is {totals.Total.Amount} {currency}.");

        var now = clock.GetUtcNow();
        var order = new Order
        {
            // The id the payment was authorised under, when checkout minted it
            // first: the provider's record and the order name the same order.
            Id = id ?? OrderId.New(),
            CustomerId = customerId,
            Mode = mode,
            ContactEmail = string.IsNullOrWhiteSpace(contactEmail) ? null : contactEmail.Trim(),
            Mandate = mandate,
            PlacedBy = placedBy,
            Channel = channel,
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
        order.lines.AddRange(lines);
        order.discounts.AddRange(discounts ?? []);
        order.taxes.AddRange(taxes ?? []);
        order.Raise(new OrderPlaced(order.Id, now));
        return order;
    }

    /// <summary>
    /// An order a sales channel took (phase 16, S3; ADR 0070). It is placed as any
    /// order is — every check, <c>Pending</c>, <c>OrderPlaced</c> so the saga holds its
    /// stock — on the day the channel says, and authorised at once with the payment the
    /// channel already captured, so nothing here captures or voids money that is not
    /// Tendero's. No new status: what a shopkeeper does with it is what they do with any.
    ///
    /// Keyed by its channel (<c>channel:shopify:…</c>), so the webhook and the
    /// reconciliation that both bring it make one order.
    /// </summary>
    public static Order Received(
        TimeProvider clock,
        OrderOrigin origin,
        OrderChannel channel,
        CustomerId customerId,
        IReadOnlyList<OrderLine> lines,
        Address shippingAddress,
        Address billingAddress,
        OrderShipping shipping,
        OrderTotals totals,
        DateTimeOffset placedAt,
        IReadOnlyList<OrderDiscount>? discounts = null,
        IReadOnlyList<OrderTax>? taxes = null,
        string culture = "es",
        string? contactEmail = null)
    {
        ArgumentNullException.ThrowIfNull(origin);
        var key = ReceivedKey(origin.Source, origin.ExternalId);

        var order = Place(
            clock, customerId, key, lines, shippingAddress, billingAddress, shipping,
            new OrderQuote(key, origin.Source, placedAt), totals, discounts, taxes, culture,
            contactEmail: contactEmail, channel: channel);
        order.ReceivedFrom = origin;
        order.CreatedAt = placedAt;
        order.AuthorizePayment(clock, new OrderPayment(origin.Source, origin.ExternalId, origin.ExternalId));
        return order;
    }

    /// <summary>The key an order a channel took is found by: the same however it arrived.</summary>
    public static string ReceivedKey(string channel, string externalId) => $"channel:{channel}:{externalId}";

    /// <summary>
    /// The channel took the money and the stock is not there (ADR 0070). The order is
    /// kept and marked: cancelling it would hide what the margin exists to bound. Only an
    /// order a channel took can be oversold; one placed here was refused at checkout.
    /// </summary>
    public void MarkOversold(TimeProvider clock, string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        if (ReceivedFrom is null)
            throw new InvalidOperationException($"Order {Id} was placed here: its stock is refused at checkout, never oversold.");

        if (Oversold is not null)
            return;

        Oversold = reason;
        UpdatedAt = clock.GetUtcNow();
        Raise(new OrderOversold(Id, reason, UpdatedAt));
    }

    /// <summary>
    /// An order that already happened on another platform (ADR 0066): born in its
    /// final state — <c>Delivered</c> or <c>Cancelled</c>, the table's other entries
    /// — on the day the source says, with every check placing makes and **no
    /// event**, because nothing must reserve, capture or write to anybody about a
    /// purchase made last year.
    ///
    /// Keyed by its source for idempotence (<c>import:shopify:5001</c>), so a store
    /// imported twice brings each order once. Its quote names the source rather
    /// than one Pricing issued: nothing will charge it, so nothing revalidates it.
    /// </summary>
    public static Order Imported(
        OrderOrigin origin,
        OrderStatus status,
        CustomerId customerId,
        IReadOnlyList<OrderLine> lines,
        Address shippingAddress,
        Address billingAddress,
        OrderShipping shipping,
        OrderTotals totals,
        DateTimeOffset placedAt,
        IReadOnlyList<OrderDiscount>? discounts = null,
        IReadOnlyList<OrderTax>? taxes = null,
        string culture = "es",
        DateTimeOffset? shippedAt = null,
        string? contactEmail = null)
    {
        ArgumentNullException.ThrowIfNull(origin);
        var key = $"import:{origin.Source}:{origin.ExternalId}";

        // History is closed: born only where the table has no edge out.
        AllowedTransitions.EnsureEntry(status, $"imported order {key}");
        if (AllowedTransitions[status].Count > 0)
            throw new InvalidOperationException($"Illegal entry {status} for imported order {key}: history is born closed.");

        var currency = EnsureConsistent(lines, shippingAddress, billingAddress, shipping, totals);

        var order = new Order
        {
            Id = OrderId.New(),
            CustomerId = customerId,
            Mode = OrderMode.Live,
            ContactEmail = string.IsNullOrWhiteSpace(contactEmail) ? null : contactEmail.Trim(),
            ImportedFrom = origin,
            IdempotencyKey = key,
            Culture = SharedKernel.Culture.Normalize(culture),
            Currency = currency,
            Status = status,
            CreatedAt = placedAt,
            // Its last change is the source's, not the import's: the returns window
            // counts from it, and a purchase made last spring is not returnable for a
            // fortnight because the shop moved platforms today.
            UpdatedAt = shippedAt ?? placedAt,
            ShippedAt = shippedAt,
            ShippingAddress = shippingAddress,
            BillingAddress = billingAddress,
            Shipping = shipping,
            Quote = new OrderQuote(key, "imported", placedAt),
            Totals = totals
        };
        order.lines.AddRange(lines);
        order.discounts.AddRange(discounts ?? []);
        order.taxes.AddRange(taxes ?? []);
        return order;
    }

    /// <summary>
    /// What an order must be, however it is born: lines with a quantity, one
    /// currency, and totals in it that add up. Returns that currency.
    /// </summary>
    private static string EnsureConsistent(
        IReadOnlyList<OrderLine> lines, Address shippingAddress, Address billingAddress,
        OrderShipping shipping, OrderTotals totals)
    {
        ArgumentNullException.ThrowIfNull(shippingAddress);
        ArgumentNullException.ThrowIfNull(billingAddress);
        ArgumentNullException.ThrowIfNull(shipping);
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

        // Prices include VAT (ADR 0028): a total is the subtotal less the
        // discounts plus the shipping, and the tax is a part of it. This is not
        // a recomputation — the order still freezes what Pricing decided — it is
        // a refusal to freeze totals that contradict themselves. The ones this
        // check would have refused are the ones the shop charged until
        // 2026-09-11, with the tax added on top of prices that already had it.
        var expected = totals.Subtotal - totals.DiscountTotal + totals.Shipping;
        if (totals.Total.Amount != expected.Amount)
            throw new InvalidOperationException(
                $"The order's totals do not add up: {totals.Subtotal} − {totals.DiscountTotal} + " +
                $"{totals.Shipping} is {expected}, not {totals.Total}.");

        return currency;
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

    public void Confirm(TimeProvider clock)
    {
        TransitionTo(clock, OrderStatus.Confirmed, now => new OrderConfirmed(Id, now));
        ConfirmedAt = UpdatedAt;
    }

    /// <summary>
    /// A test order is never shipped: it is not a transition the table lacks —
    /// a live order takes it — but a rule about which orders are real. Refused
    /// here, so no path (the backoffice, a webhook, a future carrier callback)
    /// can send a rehearsal to somebody's door.
    /// </summary>
    public void Ship(TimeProvider clock)
    {
        if (Mode == OrderMode.Test)
            throw new InvalidOperationException($"Order {Id} is a test order; a test order is never shipped.");

        TransitionTo(clock, OrderStatus.Shipped, now => new OrderShipped(Id, now));
        ShippedAt = UpdatedAt;
    }

    /// <summary>
    /// The end of a rehearsal: a confirmed test order is cancelled, and the same
    /// compensation as any cancellation gives its stock back and voids its hold.
    /// </summary>
    public void EndTest(TimeProvider clock)
    {
        if (Mode != OrderMode.Test)
            throw new InvalidOperationException($"Order {Id} is a live order; only a test order ends by itself.");

        Cancel(clock, OrderStop.TestOrderEnded, "A test order: never shipped, its stock given back.");
    }

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
