using ElGuerre.Tendero.SharedKernel;

namespace ElGuerre.Tendero.Inventory.Domain;

public enum ReservationStatus
{
    /// <summary>Stock is spoken for and still on the shelf.</summary>
    Held,

    /// <summary>The goods left: the hold became a decrement.</summary>
    Committed,

    /// <summary>Given back — the order was cancelled, or it could never be filled.</summary>
    Released,

    /// <summary>Nobody came back for it in time.</summary>
    Expired
}

public sealed record StockReserved(OrderId OrderId, DateTimeOffset OccurredAt) : IDomainEvent;

/// <summary>
/// Could not be filled, and why. The reason is the point: an order cancelled
/// with "we ran out" is a shop being honest, and one cancelled with nothing is
/// a shop that looks broken.
/// </summary>
public sealed record StockRejected(OrderId OrderId, string Reason, DateTimeOffset OccurredAt) : IDomainEvent;

/// <summary>Which warehouse a line was taken from, and how much of it.</summary>
public sealed record ReservationLine(string Sku, string WarehouseCode, int Quantity);

/// <summary>
/// One order's hold on stock, across however many warehouses it took.
///
/// It carries an <see cref="OrderId"/> and that is not a reference to Ordering:
/// strongly-typed ids live in the SharedKernel precisely so two contexts can name
/// the same thing without one having to know the other's aggregate. Inventory
/// knows there is an order with this id; it knows nothing about its status, its
/// customer or its total.
///
/// **All or nothing.** A reservation that filled four lines out of five is not a
/// partial success, it is stock held for an order that cannot ship — so a failed
/// attempt is saved as <see cref="ReservationStatus.Released"/> with its reason
/// rather than rolled back into nothing. Silence would be cheaper and it would
/// also mean the backoffice could never show why an order was cancelled.
/// </summary>
public sealed class Reservation : AggregateRoot
{
    /// <summary>
    /// A declarative state machine, the same idiom as <c>Order.AllowedTransitions</c>
    /// and <c>CombinationPolicy</c>. Four states and three edges, all of them out
    /// of <see cref="ReservationStatus.Held"/>: once a hold is resolved it stays
    /// resolved, and a transition outside this table is a bug rather than a new
    /// case.
    /// </summary>
    private static readonly Dictionary<ReservationStatus, ReservationStatus[]> AllowedTransitions = new()
    {
        [ReservationStatus.Held] = [ReservationStatus.Committed, ReservationStatus.Released, ReservationStatus.Expired],
        [ReservationStatus.Committed] = [],
        [ReservationStatus.Released] = [],
        [ReservationStatus.Expired] = []
    };

    /// <summary>
    /// How long a hold survives without being committed.
    ///
    /// It exists because the alternative is stock held forever by an order
    /// nobody paid for, which is how a shop with goods on the shelf shows
    /// "sold out". Fifteen minutes is deliberately the same as a price quote's
    /// life: the two windows answer the same question — how long the shop
    /// commits to what it said — and having them disagree would mean a cart
    /// whose price is still valid and whose stock is not.
    /// </summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(15);

    private readonly List<ReservationLine> _lines = [];

    private Reservation() { } // EF Core

    public static Reservation Hold(
        TimeProvider clock, OrderId orderId, IReadOnlyList<ReservationLine> lines)
    {
        var now = clock.GetUtcNow();

        var reservation = new Reservation
        {
            Id = Guid.CreateVersion7(),
            OrderId = orderId,
            Status = ReservationStatus.Held,
            CreatedAt = now,
            UpdatedAt = now,
            ExpiresAt = now + Lifetime
        };

        reservation._lines.AddRange(lines);
        reservation.Raise(new StockReserved(orderId, now));

        return reservation;
    }

    /// <summary>
    /// A hold that never was: nothing could be allocated, or not all of it.
    ///
    /// It is a Reservation and not a null because the refusal is the interesting
    /// row. It is what the reservations list shows, what the order's
    /// cancellation reason is copied from, and — when the demo sets both
    /// warehouses to zero — the thing there is to look at.
    /// </summary>
    public static Reservation Refused(TimeProvider clock, OrderId orderId, string reason)
    {
        var now = clock.GetUtcNow();

        var reservation = new Reservation
        {
            Id = Guid.CreateVersion7(),
            OrderId = orderId,
            Status = ReservationStatus.Released,
            Reason = reason,
            CreatedAt = now,
            UpdatedAt = now,
            ExpiresAt = now
        };

        reservation.Raise(new StockRejected(orderId, reason, now));
        return reservation;
    }

    public Guid Id { get; private set; }
    public OrderId OrderId { get; private set; }
    public ReservationStatus Status { get; private set; }

    /// <summary>Why it was released or expired. Null while it is held.</summary>
    public string? Reason { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }

    public IReadOnlyList<ReservationLine> Lines => _lines;

    public bool HasExpiredAt(DateTimeOffset at) => Status == ReservationStatus.Held && at >= ExpiresAt;

    public void Commit(TimeProvider clock) => TransitionTo(clock, ReservationStatus.Committed);

    public void Release(TimeProvider clock, string reason) =>
        TransitionTo(clock, ReservationStatus.Released, reason);

    public void Expire(TimeProvider clock) =>
        TransitionTo(clock, ReservationStatus.Expired, "The hold was not confirmed in time.");

    private void TransitionTo(TimeProvider clock, ReservationStatus target, string? reason = null)
    {
        if (!AllowedTransitions[Status].Contains(target))
            throw new InvalidOperationException($"A reservation cannot go from {Status} to {target}.");

        Status = target;
        Reason = reason;
        UpdatedAt = clock.GetUtcNow();
    }
}
