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
public sealed record OrderShipped(OrderId OrderId, DateTimeOffset OccurredAt) : IDomainEvent;

/// <summary>
/// Delivery is a fact too. It was the only transition that emitted nothing, and
/// it turns out to be exactly the one that opens the returns window: phase 4's
/// return-reason loop has nothing else to hang from.
/// </summary>
public sealed record OrderDelivered(OrderId OrderId, DateTimeOffset OccurredAt) : IDomainEvent;

public sealed class Order : AggregateRoot
{
    // A declarative state machine: a transition outside this table is a bug.
    private static readonly Dictionary<OrderStatus, OrderStatus[]> AllowedTransitions = new()
    {
        [OrderStatus.Pending]           = [OrderStatus.PaymentAuthorized, OrderStatus.PaymentFailed, OrderStatus.Cancelled],
        [OrderStatus.PaymentAuthorized] = [OrderStatus.Confirmed, OrderStatus.Cancelled],
        [OrderStatus.PaymentFailed]     = [OrderStatus.Pending, OrderStatus.Cancelled],
        [OrderStatus.Confirmed]         = [OrderStatus.Shipped, OrderStatus.Cancelled],
        [OrderStatus.Shipped]           = [OrderStatus.Delivered],
        [OrderStatus.Delivered]         = [],
        [OrderStatus.Cancelled]         = []
    };

    private readonly List<OrderLine> _lines = [];

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

    public IReadOnlyList<OrderLine> Lines => _lines;

    public Money Total => _lines.Aggregate(
        Money.Zero(Currency),
        (sum, line) => sum + line.Total);

    private Order() { } // EF Core

    public static Order Place(
        TimeProvider clock,
        CustomerId customerId, string idempotencyKey, IReadOnlyList<OrderLine> lines, string culture = "es")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);
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
            UpdatedAt = now
        };
        order._lines.AddRange(lines);
        order.Raise(new OrderPlaced(order.Id, now));
        return order;
    }

    public void AuthorizePayment(TimeProvider clock) =>
        TransitionTo(clock, OrderStatus.PaymentAuthorized, now => new OrderPaymentAuthorized(Id, now));

    public void FailPayment(TimeProvider clock, string reason) =>
        TransitionTo(clock, OrderStatus.PaymentFailed, now => new OrderPaymentFailed(Id, reason, now));

    public void Confirm(TimeProvider clock) =>
        TransitionTo(clock, OrderStatus.Confirmed, now => new OrderConfirmed(Id, now));

    public void Ship(TimeProvider clock) =>
        TransitionTo(clock, OrderStatus.Shipped, now => new OrderShipped(Id, now));

    public void Deliver(TimeProvider clock) =>
        TransitionTo(clock, OrderStatus.Delivered, now => new OrderDelivered(Id, now));

    // The compensation saga calls here when something fails halfway (payment
    // authorized but no stock, say): it cancels and releases.
    public void Cancel(TimeProvider clock, string reason) =>
        TransitionTo(clock, OrderStatus.Cancelled, now => new OrderCancelled(Id, reason, now));

    // The factory stopped being nullable when Deliver() started emitting its
    // event: the `IDomainEvent?` existed for one mute transition.
    private void TransitionTo(
        TimeProvider clock, OrderStatus target, Func<DateTimeOffset, IDomainEvent> eventFactory)
    {
        if (!AllowedTransitions[Status].Contains(target))
            throw new InvalidOperationException($"Illegal transition {Status} -> {target} for order {Id}.");

        Status = target;
        UpdatedAt = clock.GetUtcNow();

        Raise(eventFactory(UpdatedAt));
    }
}
